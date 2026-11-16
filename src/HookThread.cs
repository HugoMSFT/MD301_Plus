using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace MD301Plus
{
    // Low-level hooks must keep pumping messages even when the editor or a shell operation is busy.
    internal sealed class HookThread : IDisposable
    {
        private readonly Thread thread;
        private readonly SynchronizationContext owner;
        private Control dispatcher;
        private volatile bool disposed;
        private int stopped;

        internal HookThread(Action install, Action release)
        {
            owner = new WindowsFormsSynchronizationContext();
            Exception startupError = null;
            int ready = 0;
            thread = new Thread(delegate()
            {
                bool started = false;
                try
                {
                    using (Control window = new Control())
                    {
                        dispatcher = window;
                        window.CreateControl();
                        if (window.Handle == IntPtr.Zero)
                            throw new InvalidOperationException("The input message window could not be created.");
                        install();
                        window.BeginInvoke(new Action(delegate
                        {
                            started = true;
                            Interlocked.Exchange(ref ready, 1);
                        }));
                        Application.Run();
                        if (!disposed && started)
                            ReportFailure(new InvalidOperationException("The input message loop stopped unexpectedly."));
                    }
                }
                catch (Exception error)
                {
                    if (!started) startupError = error;
                    else ReportFailure(error);
                }
                finally
                {
                    try { release(); }
                    catch (Exception error) { ReportFailure(error); }
                    finally
                    {
                        Interlocked.Exchange(ref stopped, 1);
                        if (!started)
                        {
                            if (startupError == null)
                                startupError = new InvalidOperationException("The input message loop stopped before it became ready.");
                            Interlocked.Exchange(ref ready, 1);
                        }
                    }
                }
            });
            thread.Name = "MD301 Plus input";
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            // STA waits such as Join/WaitOne can pump UI messages and reenter ring creation or disposal.
            while (Thread.VolatileRead(ref ready) == 0) Thread.Sleep(1);
            if (startupError != null) ExceptionDispatchInfo.Capture(startupError).Throw();
        }

        internal bool IsRunning { get { return !disposed && Thread.VolatileRead(ref stopped) == 0 && thread.IsAlive; } }

        private void ReportFailure(Exception error)
        {
            AppLog.Error("Input listener stopped", error);
            try
            {
                if (!disposed) owner.Post(delegate
                {
                    if (!disposed) AppLog.ShowError(null, "Input listener stopped",
                        new InvalidOperationException("Save your button assignment again, or restart MD301 Plus.", error));
                }, null);
            }
            catch (InvalidOperationException dispatchError)
            {
                if (!disposed) AppLog.Error("Reporting an input listener failure", dispatchError);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (thread.IsAlive)
            {
                try { dispatcher.BeginInvoke(new Action(Application.ExitThread)); }
                catch (InvalidOperationException error)
                {
                    if (thread.IsAlive) AppLog.Error("Stopping the input listener", error);
                }
                Stopwatch wait = Stopwatch.StartNew();
                while (Thread.VolatileRead(ref stopped) == 0 && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
                if (Thread.VolatileRead(ref stopped) == 0)
                    AppLog.Error("Stopping the input listener", new TimeoutException("The input thread did not stop within five seconds."));
            }
        }
    }
}
