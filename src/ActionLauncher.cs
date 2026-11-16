using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MD301Plus
{
    internal sealed class ActionLauncher : IDisposable
    {
        private readonly SynchronizationContext context;
        private readonly Action<string> launch;
        private readonly Func<string, bool> exists;
        private readonly Action<Exception> completed;
        private int busy;
        private volatile bool disposed;

        internal ActionLauncher(Action<Exception> completed) : this(ShellLauncher.Open, completed) { }

        internal ActionLauncher(Action<string> launch, Action<Exception> completed, Func<string, bool> exists = null)
        {
            this.launch = launch;
            this.completed = completed;
            this.exists = exists ?? delegate(string path) { return File.Exists(path) || Directory.Exists(path); };
            context = new WindowsFormsSynchronizationContext();
        }

        internal bool IsBusy { get { return Interlocked.CompareExchange(ref busy, 0, 0) != 0; } }

        internal void Open(string target)
        {
            if (disposed) throw new ObjectDisposedException("ActionLauncher");
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                throw new InvalidOperationException("Another launch is still in progress. Wait for it to finish, then try again.");
            Thread worker = new Thread(delegate() { Run(target); });
            worker.Name = "MD301 Plus launcher";
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            try { worker.Start(); }
            catch
            {
                Interlocked.Exchange(ref busy, 0);
                throw;
            }
        }

        private void Run(string target)
        {
            Exception failure = null;
            try
            {
                try
                {
                    RingAction.ValidateLaunchTarget(target);
                    Uri url;
                    bool web = Uri.TryCreate(target, UriKind.Absolute, out url) &&
                        (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
                    if (!web && !exists(target))
                        throw new FileNotFoundException("The launch target does not exist: " + target);
                    if (!disposed) launch(target);
                }
                // This boundary must report failures on the UI thread, not terminate the process from a worker.
                catch (Exception error) { failure = error; }
                if (disposed)
                {
                    if (failure != null) AppLog.Error("Launch failed during shutdown", failure);
                }
            }
            finally { Interlocked.Exchange(ref busy, 0); }
            if (disposed) return;
            try { context.Post(delegate { if (!disposed) completed(failure); }, null); }
            catch (InvalidOperationException error)
            {
                if (!disposed) AppLog.Error("Reporting an application launch", error);
            }
        }

        public void Dispose() { disposed = true; }
    }
}
