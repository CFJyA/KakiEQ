using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;

namespace KakiEQ_WinUI.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? MainWindow.Instance?.DispatcherQueue;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            RunOnUIThread(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        public void RunOnUIThread(Action action)
        {
            var dq = _dispatcherQueue ?? MainWindow.Instance?.DispatcherQueue;
            if (dq != null && !dq.HasThreadAccess)
            {
                dq.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }
    }
}

