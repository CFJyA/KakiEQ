import sys
import io
import os
import wave
import urllib.request
import numpy as np
import torch
import sphn
import julius

try:
    import onnxruntime as ort
    HAS_ORT = True
except ImportError:
    HAS_ORT = False

def report_progress(pct):
    sys.stdout.write(f"PROGRESS:{pct}\n")
    sys.stdout.flush()

def ensure_model_path():
    app_models = os.path.expandvars('%LOCALAPPDATA%/KakiEQ/Models')
    os.makedirs(app_models, exist_ok=True)
    model_path = os.path.join(app_models, 'UVR_MDXNET_1_9703.onnx')

    if not os.path.exists(model_path):
        report_progress(2)
        url = 'https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR_MDXNET_1_9703.onnx'
        sys.stderr.write("Descargando modelo ultrarrápido UVR_MDXNET_1_9703 (28 MB)...\n")
        urllib.request.urlretrieve(url, model_path)
    
    return model_path

class MdxKaraokeEngine:
    def __init__(self):
        self.sess = None
        self.n_fft = 6144
        self.hop_length = 1024
        self.dim_f = 2048
        self.dim_t = 256
        self.trim = self.n_fft // 2
        self.chunk_size = self.hop_length * (self.dim_t - 1)
        self.gen_size = self.chunk_size - 2 * self.trim
        self.hann_window = torch.hann_window(self.n_fft, periodic=True)

    def initialize(self):
        model_path = ensure_model_path()
        opts = ort.SessionOptions()
        opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL

        # Try DirectML GPU acceleration first, fallback to CPU
        providers = ['DmlExecutionProvider', 'CPUExecutionProvider']
        try:
            self.sess = ort.InferenceSession(model_path, sess_options=opts, providers=providers)
        except Exception:
            self.sess = ort.InferenceSession(model_path, sess_options=opts, providers=['CPUExecutionProvider'])

        # Warmup GPU shaders
        dummy = np.random.randn(1, 4, self.dim_f, self.dim_t).astype(np.float32)
        self.sess.run(['output'], {'input': dummy})

    def separate(self, input_path, output_path):
        report_progress(5)

        wav, sr = sphn.read(input_path)
        if wav.ndim == 1:
            wav = np.stack([wav, wav])
        elif wav.shape[0] > 2:
            wav = wav[:2]

        if sr != 44100:
            wav_tensor = torch.from_numpy(wav).float()
            wav_tensor = julius.resample_frac(wav_tensor, sr, 44100)
            wav = wav_tensor.numpy()
            sr = 44100

        peak = np.abs(wav).max()
        if peak > 0:
            wav = wav / peak

        report_progress(10)

        pad = self.gen_size + self.trim - (wav.shape[-1] % self.gen_size)
        mixture = np.concatenate([np.zeros((2, self.trim), dtype=np.float32), wav, np.zeros((2, pad), dtype=np.float32)], axis=1)
        n_chunks = (mixture.shape[-1] - self.trim) // self.gen_size

        report_progress(15)

        out_chunks = []
        for i in range(n_chunks):
            start = i * self.gen_size
            chunk = mixture[:, start : start + self.chunk_size]
            if chunk.shape[1] < self.chunk_size:
                chunk = np.pad(chunk, ((0, 0), (0, self.chunk_size - chunk.shape[1])))

            w = torch.from_numpy(chunk)
            s = torch.stft(w, n_fft=self.n_fft, hop_length=self.hop_length, window=self.hann_window, center=True, return_complex=True)
            s = torch.view_as_real(s)
            s = s.permute([0, 3, 1, 2]).reshape([1, 4, s.shape[1], s.shape[2]])
            spek = s[:, :, :self.dim_f, :self.dim_t].numpy()
            spek[:, :, :3, :] = 0

            pred = self.sess.run(['output'], {'input': spek})[0]

            pred_t = torch.from_numpy(pred)
            num_bins = self.n_fft // 2 + 1
            if self.dim_f < num_bins:
                pad_t = torch.zeros(1, 4, num_bins - self.dim_f, self.dim_t)
                pred_t = torch.cat([pred_t, pad_t], dim=2)

            left_c = torch.complex(pred_t[0, 0], pred_t[0, 1])
            right_c = torch.complex(pred_t[0, 2], pred_t[0, 3])
            c = torch.stack([left_c, right_c])
            inv = torch.istft(c, n_fft=self.n_fft, hop_length=self.hop_length, window=self.hann_window, center=True, length=self.chunk_size)
            out_chunks.append(inv[:, self.trim : self.trim + self.gen_size].numpy())

            pct = min(95, int(15 + 80 * ((i + 1) / n_chunks)))
            report_progress(pct)

        vocals = np.concatenate(out_chunks, axis=1)[:, :wav.shape[-1]]

        # Invert: Instrumental = mix - (vocals * compensation)
        instrumental = wav - (vocals * 1.035)
        if peak > 0:
            instrumental = instrumental * peak

        instrumental = np.clip(instrumental, -1.0, 1.0)
        report_progress(97)

        # Write clean WAV container to output_path
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
        sys.stdout.write(f"SUCCESS\t{output_path}\n")
        sys.stdout.flush()

def run_daemon():
    engine = MdxKaraokeEngine()
    engine.initialize()
    sys.stdout.write("DAEMON_READY\n")
    sys.stdout.flush()

    while True:
        line = sys.stdin.readline()
        if not line:
            break
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break

        if line.startswith("PROCESS\t"):
            parts = line[8:].split("\t")
            if len(parts) >= 2:
                in_path, out_path = parts[0], parts[1]
                try:
                    engine.separate(in_path, out_path)
                except Exception as e:
                    sys.stdout.write(f"ERROR\t{str(e)}\n")
                    sys.stdout.flush()

def main():
    if len(sys.argv) >= 3:
        # CLI single execution mode
        in_path, out_path = sys.argv[1], sys.argv[2]
        engine = MdxKaraokeEngine()
        engine.initialize()
        engine.separate(in_path, out_path)
    else:
        # Persistent daemon mode
        run_daemon()

if __name__ == "__main__":
    main()
