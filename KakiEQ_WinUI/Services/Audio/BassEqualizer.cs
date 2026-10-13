using System;
using ManagedBass;
using ManagedBass.DirectX8;

namespace KakiEQ_WinUI.Services.Audio
{
    public class BassEqualizer : EqualizerBase
    {
        private static readonly float[] DefaultBands = new float[]
        {
            32f, 64f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
        };

        private readonly float[] _gains = new float[10];
        private readonly int[] _fxHandles = new int[10];
        private int _stream = 0;
        private bool _isEnabled = true;

        public override float[] Bands => DefaultBands;
        public override float MinGain => -15f;
        public override float MaxGain => 15f;

        public override bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                ApplyAllBands();
            }
        }

        public void AttachToStream(int stream)
        {
            _stream = stream;
            for (int i = 0; i < _fxHandles.Length; i++)
            {
                _fxHandles[i] = 0;
            }

            if (_stream != 0)
            {
                for (int i = 0; i < Bands.Length; i++)
                {
                    int fx = Bass.ChannelSetFX(_stream, EffectType.DXParamEQ, i);
                    _fxHandles[i] = fx;
                    if (fx != 0)
                    {
                        var eq = new DXParamEQParameters
                        {
                            fCenter = Bands[i],
                            fBandwidth = 18f,
                            fGain = _isEnabled ? _gains[i] : 0f
                        };
                        Bass.FXSetParameters(fx, eq);
                    }
                }
            }
        }

        public override void SetBandGain(int bandIndex, float gain)
        {
            if (bandIndex < 0 || bandIndex >= _gains.Length) return;

            _gains[bandIndex] = Math.Clamp(gain, MinGain, MaxGain);

            if (_stream != 0 && _fxHandles[bandIndex] != 0 && _isEnabled)
            {
                var eq = new DXParamEQParameters
                {
                    fCenter = Bands[bandIndex],
                    fBandwidth = 18f,
                    fGain = _gains[bandIndex]
                };
                Bass.FXSetParameters(_fxHandles[bandIndex], eq);
            }
        }

        public override float GetBandGain(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= _gains.Length) return 0f;
            return _gains[bandIndex];
        }

        public override void Reset()
        {
            for (int i = 0; i < _gains.Length; i++)
            {
                SetBandGain(i, 0f);
            }
        }

        private void ApplyAllBands()
        {
            if (_stream == 0) return;
            for (int i = 0; i < Bands.Length; i++)
            {
                if (_fxHandles[i] != 0)
                {
                    var eq = new DXParamEQParameters
                    {
                        fCenter = Bands[i],
                        fBandwidth = 18f,
                        fGain = _isEnabled ? _gains[i] : 0f
                    };
                    Bass.FXSetParameters(_fxHandles[i], eq);
                }
            }
        }
    }
}

