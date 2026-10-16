using System;
using System.IO;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.UI.Dispatching;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.SystemIntegration
{
    public class SmtcService : IDisposable
    {
        private readonly MediaPlayer _player;
        private readonly SystemMediaTransportControls _smtc;
        private readonly DispatcherQueue _dispatcherQueue;

        public event Action? PlayRequested;
        public event Action? PauseRequested;
        public event Action? NextRequested;
        public event Action? PreviousRequested;

        public SmtcService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _player = new MediaPlayer();
            _player.CommandManager.IsEnabled = false; // We handle commands manually

            _smtc = _player.SystemMediaTransportControls;
            _smtc.IsEnabled = true;
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsNextEnabled = true;
            _smtc.IsPreviousEnabled = true;

            _smtc.ButtonPressed += Smtc_ButtonPressed;
        }

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        PlayRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        PauseRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        NextRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        PreviousRequested?.Invoke();
                        break;
                }
            });
        }

        public void UpdatePlaybackState(bool isPlaying)
        {
            try
            {
                _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] Error updating status: {ex.Message}");
            }
        }

        public async void UpdateMetadata(Track? track, bool isPlaying)
        {
            try
            {
                if (track == null)
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    return;
                }

                _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileName(track.FilePath) : track.Title;
                updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "Kaki EQ" : track.Artist;
                updater.MusicProperties.AlbumTitle = string.IsNullOrWhiteSpace(track.Album) ? string.Empty : track.Album;

                if (!string.IsNullOrEmpty(track.CoverImagePath) && File.Exists(track.CoverImagePath))
                {
                    try
                    {
                        var storageFile = await StorageFile.GetFileFromPathAsync(track.CoverImagePath);
                        updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(storageFile);
                    }
                    catch
                    {
                        updater.Thumbnail = null;
                    }
                }
                else
                {
                    updater.Thumbnail = null;
                }

                updater.Update();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] Error updating metadata: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _smtc.ButtonPressed -= Smtc_ButtonPressed;
                _smtc.IsEnabled = false;
                _player.Dispose();
            }
            catch { }
        }
    }
}
