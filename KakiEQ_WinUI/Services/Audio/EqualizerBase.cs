using System;

namespace KakiEQ_WinUI.Services.Audio
{
    public abstract class EqualizerBase
    {
        public abstract float[] Bands { get; }
        public abstract float MinGain { get; }
        public abstract float MaxGain { get; }
        public abstract bool IsEnabled { get; set; }
        public abstract void SetBandGain(int bandIndex, float gain);
        public abstract float GetBandGain(int bandIndex);
        public abstract void Reset();
    }
}

