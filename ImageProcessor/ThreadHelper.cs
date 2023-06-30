using System;
using System.Windows;
using System.Windows.Threading;

namespace ImageProcessor
{
    public class ThreadHelper
    {
        public static void OnUIThread(Action action)
        {
            Application.Current.Dispatcher.Invoke(
                () =>
                {
                    action();
                }, DispatcherPriority.Background);
        }
    }
}
