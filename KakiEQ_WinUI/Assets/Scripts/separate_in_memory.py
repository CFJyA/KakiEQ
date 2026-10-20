import sys
import io
import os
import wave
import urllib.request
import numpy as np
import torch
import sphn
import julius

# Try to use ONNX Runtime with DirectML GPU acceleration
try:
    import onnxruntime as ort
    HAS_ORT = True
except ImportError:
    HAS_ORT = False

def report_progress(pct):
    sys.stderr.write(f"PROGRESS:{pct}\n")
    sys.stderr.flush()

def ensure_model_path():
    app_models = os.path.expandvars('%LOCALAPPDATA%/KakiEQ/Models')
    os.makedirs(app_models, exist_ok=True)
    model_path = os.path.join(app_models, 'UVR-MDX-NET-Inst_HQ_3.onnx')

    if not os.path.exists(model_path):
        # Also check cache folder
        cache_path = os.path.expanduser('~/.cache/onnx_models/UVR-MDX-NET-Inst_HQ_3.onnx')
        if os.path.exists(cache_path):
            import shutil
            shutil.copyfile(cache_path, model_path)
            return model_path

        report_progress(2)
        url = 'https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR-MDX-NET-Inst_HQ_3.onnx'
        sys.stderr.write("Descargando modelo de alta velocidad UVR-MDX-NET (63 MB)...\n")
        urllib.request.urlretrieve(url, model_path)
    
    return model_path

def separate_with_mdx(input_path, output_path, model_path):
    report_progress(5)
    
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    
    # Try DirectML first (AMD / Intel GPU acceleration), fallback to multi-threaded CPU
    providers = ['DmlExecutionProvider', 'CPUExecutionProvider']
    try:
        sess = ort.InferenceSession(model_path, sess_options=opts, providers=providers)
    except Exception:
        sess = ort.InferenceSession(model_path, sess_options=opts, providers=['CPUExecutionProvider'])

    report_progress(10)
    
    # Read audio
    wav, sr = sphn.read(input_path)
    if wav.ndim == 1:
        wav = np.stack([wav, wav])
    elif wav.shape[0] > 2:
        wav = wav[:2]

    # Model requires 44.1 kHz
    if sr != 44100:
        wav_tensor = torch.from_numpy(wav).float()
        wav_tensor = julius.resample_frac(wav_tensor, sr, 44100)
        wav = wav_tensor.numpy()
        sr = 44100

    peak = np.abs(wav).max()
    if peak > 0:
        wav = wav / peak

    n_fft = 6144
    hop_length = 1024
    dim_f = 3072
    dim_t = 256
    trim = n_fft // 2
    chunk_size = hop_length * (dim_t - 1)
    gen_size = chunk_size - 2 * trim

    hann_window = torch.hann_window(n_fft, periodic=True)

    def stft(wave_chunk):
        w = torch.from_numpy(wave_chunk)
        s = torch.stft(w, n_fft=n_fft, hop_length=hop_length, window=hann_window, center=True, return_complex=True)
        s = torch.view_as_real(s)
        s = s.permute([0, 3, 1, 2]).reshape([1, 4, s.shape[1], s.shape[2]])
        return s[:, :, :dim_f, :dim_t].numpy()

    def istft(spek, length):
        s = torch.from_numpy(spek)
        num_bins = n_fft // 2 + 1
        if dim_f < num_bins:
            pad = torch.zeros(1, 4, num_bins - dim_f, dim_t)
            s = torch.cat([s, pad], dim=2)
        left_c = torch.complex(s[0, 0], s[0, 1])
        right_c = torch.complex(s[0, 2], s[0, 3])
        c = torch.stack([left_c, right_c])
        inv = torch.istft(c, n_fft=n_fft, hop_length=hop_length, window=hann_window, center=True, length=length)
        return inv.numpy()

    pad = gen_size + trim - (wav.shape[-1] % gen_size)
    mixture = np.concatenate([np.zeros((2, trim), dtype=np.float32), wav, np.zeros((2, pad), dtype=np.float32)], axis=1)

    n_chunks = (mixture.shape[-1] - trim) // gen_size
    report_progress(15)

    out_chunks = []
    for i in range(n_chunks):
        start = i * gen_size
        chunk = mixture[:, start : start + chunk_size]
        if chunk.shape[1] < chunk_size:
            chunk = np.pad(chunk, ((0, 0), (0, chunk_size - chunk.shape[1])))
        
        spek = stft(chunk)
        spek[:, :, :3, :] = 0 # High-pass filter noise threshold
        pred = sess.run(['output'], {'input': spek})[0]
        inv = istft(pred, chunk_size)
        out_chunks.append(inv[:, trim : trim + gen_size])

        pct = min(95, int(15 + 80 * ((i + 1) / n_chunks)))
        report_progress(pct)

    result = np.concatenate(out_chunks, axis=1)[:, :wav.shape[-1]]
    if peak > 0:
        result = result * peak

    # Soft clamp
    result = np.clip(result, -1.0, 1.0)
    report_progress(97)

    # Encode to 16-bit PCM WAV into output_path
    int_audio = (result * 32767.0).astype(np.int16)
    interleaved = np.ascontiguousarray(int_audio.T)

    out_dir = os.path.dirname(output_path)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir, exist_ok=True)

    temp_out = output_path + ".tmp"
    with wave.open(temp_out, 'wb') as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(interleaved.tobytes())

    if os.path.exists(output_path):
        try:
            os.remove(output_path)
        except Exception:
            pass
    os.rename(temp_out, output_path)

    report_progress(100)
    sys.stderr.write("SUCCESS\n")
    sys.stderr.flush()

def main():
    if len(sys.argv) < 3:
        sys.stderr.write("ERROR: Missing arguments. Usage: python separate_in_memory.py <input_audio> <output_kinst>\n")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]

    try:
        if HAS_ORT:
            model_path = ensure_model_path()
            if os.path.exists(model_path):
                separate_with_mdx(input_path, output_path, model_path)
                return

        # Fallback to Demucs if ONNX Runtime model is unavailable
        from demucs.apply import apply_model
        from demucs.pretrained import get_model
        report_progress(5)
        model = get_model('htdemucs')
        model.eval()

        wav, sr = sphn.read(input_path)
        if wav.ndim == 1:
            wav = np.stack([wav, wav])
        elif wav.shape[0] > 2:
            wav = wav[:2]

        wav_tensor = torch.from_numpy(wav).float()
        if sr != model.samplerate:
            wav_tensor = julius.resample_frac(wav_tensor, sr, model.samplerate)
            sr = model.samplerate

        ref = wav_tensor.mean(0)
        wav_tensor = (wav_tensor - ref.mean()) / (ref.std() + 1e-8)
        wav_tensor = wav_tensor.unsqueeze(0)

        with torch.inference_mode():
            sources = apply_model(model, wav_tensor, device='cpu', split=True, overlap=0.1, shifts=0)

        instrumental = sources[0, :3].sum(0)
        instrumental = instrumental * (ref.std() + 1e-8) + ref.mean()
        instrumental = instrumental.clamp(-1.0, 1.0).cpu().numpy()

        int_audio = (instrumental * 32767.0).astype(np.int16)
        interleaved = np.ascontiguousarray(int_audio.T)

        out_dir = os.path.dirname(output_path)
        if out_dir and not os.path.exists(out_dir):
            os.makedirs(out_dir, exist_ok=True)

        temp_out = output_path + ".tmp"
        with wave.open(temp_out, 'wb') as wf:
            wf.setnchannels(2)
            wf.setsampwidth(2)
            wf.setframerate(sr)
            wf.writeframes(interleaved.tobytes())

        if os.path.exists(output_path):
            try: os.remove(output_path)
            except Exception: pass
        os.rename(temp_out, output_path)

        report_progress(100)
        sys.stderr.write("SUCCESS\n")
        sys.stderr.flush()

    except Exception as e:
        sys.stderr.write(f"ERROR: {str(e)}\n")
        sys.stderr.flush()
        sys.exit(1)

if __name__ == "__main__":
    main()
