using System;

namespace KakiEQ_WinUI.Models
{
    public class DeviceEqProfile
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = "Plano";
        public bool IsEnabled { get; set; } = true;
        public float PreampGain { get; set; } = 0f;
        public float[] Gains { get; set; } = new float[10];
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        public DeviceEqProfile Clone()
        {
            var copy = new DeviceEqProfile
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                ProfileName = ProfileName,
                IsEnabled = IsEnabled,
                PreampGain = PreampGain,
                LastModified = LastModified,
                Gains = new float[10]
            };
            if (Gains != null)
            {
                Array.Copy(Gains, copy.Gains, Math.Min(Gains.Length, 10));
            }
            return copy;
        }
    }
}
