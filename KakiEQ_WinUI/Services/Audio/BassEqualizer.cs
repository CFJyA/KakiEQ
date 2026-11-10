using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ManagedBass;
using ManagedBass.DirectX8;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Audio
{
    public class BassEqualizer : EqualizerBase
    {
        private static readonly float[] DefaultBands = new float[]
        {
            32f, 64f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
        };

        private readonly float[] _masterGains = new float[10];
        private readonly int[] _masterFxHandles = new int[10];
        private int _stream = 0;
        private bool _isEnabled = true;
        private float _masterPreampGain = 0f;

        private readonly ConcurrentDictionary<string, AudioOutputDevice> _registeredDevices = new(StringComparer.OrdinalIgnoreCase);

        public override float[] Bands => DefaultBands;
        public override float MinGain => -15f;
        public override float MaxGain => 15f;

        public float MasterPreampGain
        {
            get => _masterPreampGain;
            set => _masterPreampGain = Math.Clamp(value, -15f, 15f);
        }

        public override bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                ApplyAllMasterBands();
                UpdateMasterLinkedDevices();
            }
        }

        public void RegisterDevice(AudioOutputDevice device)
        {
            if (device != null && !string.IsNullOrEmpty(device.Id))
            {
                _registeredDevices[device.Id] = device;
            }
        }

        public void UnregisterDevice(AudioOutputDevice device)
        {
            if (device != null && !string.IsNullOrEmpty(device.Id))
            {
                _registeredDevices.TryRemove(device.Id, out _);
            }
        }

        public void AttachToStream(int stream)
        {
            _stream = stream;
            for (int i = 0; i < _masterFxHandles.Length; i++)
            {
                _masterFxHandles[i] = 0;
            }

            if (_stream != 0)
            {
                for (int i = 0; i < Bands.Length; i++)
                {
                    int fx = Bass.ChannelSetFX(_stream, EffectType.DXParamEQ, i);
                    _masterFxHandles[i] = fx;
                    if (fx != 0)
                    {
                        var eq = new DXParamEQParameters
                        {
                            fCenter = Bands[i],
                            fBandwidth = 18f,
                            fGain = _isEnabled ? _masterGains[i] : 0f
                        };
                        Bass.FXSetParameters(fx, eq);
                    }
                }
            }
        }

        public void AttachDevicePushStream(AudioOutputDevice device, int pushStreamHandle)
        {
            if (device == null || pushStreamHandle == 0) return;

            RegisterDevice(device);
            for (int i = 0; i < Bands.Length; i++)
            {
                int fx = Bass.ChannelSetFX(pushStreamHandle, EffectType.DXParamEQ, i);
                device.PushEqFxHandles[i] = fx;
                if (fx != 0)
                {
                    float gain = GetEffectiveGainForDevice(device, i);
                    var eq = new DXParamEQParameters
                    {
                        fCenter = Bands[i],
                        fBandwidth = 18f,
                        fGain = gain
                    };
                    Bass.FXSetParameters(fx, eq);
                }
            }
        }

        public void AttachDeviceRoutingPushStream(AudioOutputDevice device, int routingPushStreamHandle)
        {
            if (device == null || routingPushStreamHandle == 0) return;

            RegisterDevice(device);
            for (int i = 0; i < Bands.Length; i++)
            {
                int fx = Bass.ChannelSetFX(routingPushStreamHandle, EffectType.DXParamEQ, i);
                device.RoutingEqFxHandles[i] = fx;
                if (fx != 0)
                {
                    float gain = GetEffectiveGainForDevice(device, i);
                    var eq = new DXParamEQParameters
                    {
                        fCenter = Bands[i],
                        fBandwidth = 18f,
                        fGain = gain
                    };
                    Bass.FXSetParameters(fx, eq);
                }
            }
        }

        public void DetachDevicePushStream(AudioOutputDevice device)
        {
            if (device == null) return;
            for (int i = 0; i < device.PushEqFxHandles.Length; i++)
            {
                device.PushEqFxHandles[i] = 0;
            }
        }

        public void DetachDeviceRoutingPushStream(AudioOutputDevice device)
        {
            if (device == null) return;
            for (int i = 0; i < device.RoutingEqFxHandles.Length; i++)
            {
                device.RoutingEqFxHandles[i] = 0;
            }
        }

        private float GetEffectiveGainForDevice(AudioOutputDevice device, int bandIndex)
        {
            bool enabled = device.EqIsEnabled;
            if (!enabled) return 0f;

            if (device.EqProfileName == "Master (Global)" || device.EqProfileName == "🌐 Master")
            {
                return _isEnabled ? _masterGains[bandIndex] : 0f;
            }

            return (device.EqGains != null && device.EqGains.Length > bandIndex)
                ? device.EqGains[bandIndex]
                : 0f;
        }

        public override void SetBandGain(int bandIndex, float gain)
        {
            if (bandIndex < 0 || bandIndex >= _masterGains.Length) return;

            _masterGains[bandIndex] = Math.Clamp(gain, MinGain, MaxGain);

            if (_stream != 0 && _masterFxHandles[bandIndex] != 0 && _isEnabled)
            {
                var eq = new DXParamEQParameters
                {
                    fCenter = Bands[bandIndex],
                    fBandwidth = 18f,
                    fGain = _masterGains[bandIndex]
                };
                Bass.FXSetParameters(_masterFxHandles[bandIndex], eq);
            }

            // Update all devices linked to Master
            UpdateMasterLinkedBand(bandIndex);
        }

        public override float GetBandGain(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= _masterGains.Length) return 0f;
            return _masterGains[bandIndex];
        }

        public override void Reset()
        {
            for (int i = 0; i < _masterGains.Length; i++)
            {
                SetBandGain(i, 0f);
            }
        }

        public void SetDeviceBandGain(AudioOutputDevice device, int bandIndex, float gain)
        {
            if (device == null || bandIndex < 0 || bandIndex >= Bands.Length) return;

            if (device.EqGains == null || device.EqGains.Length < 10)
            {
                device.EqGains = new float[10];
            }

            device.EqGains[bandIndex] = Math.Clamp(gain, MinGain, MaxGain);

            float effectiveGain = device.EqIsEnabled ? device.EqGains[bandIndex] : 0f;
            var eq = new DXParamEQParameters
            {
                fCenter = Bands[bandIndex],
                fBandwidth = 18f,
                fGain = effectiveGain
            };

            int pushFx = device.PushEqFxHandles[bandIndex];
            if (pushFx != 0)
            {
                Bass.FXSetParameters(pushFx, eq);
            }

            int routingFx = device.RoutingEqFxHandles[bandIndex];
            if (routingFx != 0)
            {
                Bass.FXSetParameters(routingFx, eq);
            }

            if (device.IsPrimary && _stream != 0 && _masterFxHandles[bandIndex] != 0 && device.EqProfileName != "Master (Global)")
            {
                Bass.FXSetParameters(_masterFxHandles[bandIndex], eq);
            }
        }

        public void ApplyAllDeviceBands(AudioOutputDevice device)
        {
            if (device == null) return;

            for (int i = 0; i < Bands.Length; i++)
            {
                float gain = GetEffectiveGainForDevice(device, i);
                var eq = new DXParamEQParameters
                {
                    fCenter = Bands[i],
                    fBandwidth = 18f,
                    fGain = gain
                };

                int pushFx = device.PushEqFxHandles[i];
                if (pushFx != 0)
                {
                    Bass.FXSetParameters(pushFx, eq);
                }

                int routingFx = device.RoutingEqFxHandles[i];
                if (routingFx != 0)
                {
                    Bass.FXSetParameters(routingFx, eq);
                }

                if (device.IsPrimary && _stream != 0 && _masterFxHandles[i] != 0 && device.EqProfileName != "Master (Global)")
                {
                    Bass.FXSetParameters(_masterFxHandles[i], eq);
                }
            }
        }

        private void ApplyAllMasterBands()
        {
            if (_stream != 0)
            {
                for (int i = 0; i < Bands.Length; i++)
                {
                    if (_masterFxHandles[i] != 0)
                    {
                        var eq = new DXParamEQParameters
                        {
                            fCenter = Bands[i],
                            fBandwidth = 18f,
                            fGain = _isEnabled ? _masterGains[i] : 0f
                        };
                        Bass.FXSetParameters(_masterFxHandles[i], eq);
                    }
                }
            }
        }

        private void UpdateMasterLinkedBand(int bandIndex)
        {
            float masterGain = _isEnabled ? _masterGains[bandIndex] : 0f;
            var eq = new DXParamEQParameters
            {
                fCenter = Bands[bandIndex],
                fBandwidth = 18f,
                fGain = masterGain
            };

            foreach (var dev in _registeredDevices.Values)
            {
                if (dev.EqProfileName == "Master (Global)" || dev.EqProfileName == "🌐 Master")
                {
                    int pushFx = dev.PushEqFxHandles[bandIndex];
                    if (pushFx != 0)
                    {
                        Bass.FXSetParameters(pushFx, eq);
                    }
                    int routingFx = dev.RoutingEqFxHandles[bandIndex];
                    if (routingFx != 0)
                    {
                        Bass.FXSetParameters(routingFx, eq);
                    }
                }
            }
        }

        private void UpdateMasterLinkedDevices()
        {
            foreach (var dev in _registeredDevices.Values)
            {
                if (dev.EqProfileName == "Master (Global)" || dev.EqProfileName == "🌐 Master")
                {
                    ApplyAllDeviceBands(dev);
                }
            }
        }
    }
}
