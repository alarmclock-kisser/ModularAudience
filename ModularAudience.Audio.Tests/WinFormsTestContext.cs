using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModularAudience.Audio.Tests
{
    /// <summary>Keeps async UI callbacks on the test's STA and reports native-window errors as test failures.</summary>
    internal sealed class WinFormsTestContext : IDisposable
    {
        private readonly SynchronizationContext? previousContext;
        private readonly WindowsFormsSynchronizationContext context;
        private readonly bool previousCrossThreadChecks;
        private readonly ConcurrentQueue<Exception> exceptions = new();
        private readonly int threadId = Environment.CurrentManagedThreadId;

        internal WinFormsTestContext()
        {
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            this.previousContext = SynchronizationContext.Current;
            this.previousCrossThreadChecks = Control.CheckForIllegalCrossThreadCalls;
            this.context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(this.context);
            Control.CheckForIllegalCrossThreadCalls = true;
            Application.ThreadException += this.OnThreadException;
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs args) => this.exceptions.Enqueue(args.Exception);

        public void Dispose()
        {
            Assert.AreEqual(this.threadId, Environment.CurrentManagedThreadId, "UI test cleanup must run on its owning STA.");
            try
            {
                Application.DoEvents();
            }
            finally
            {
                Application.ThreadException -= this.OnThreadException;
                SynchronizationContext.SetSynchronizationContext(this.previousContext);
                // WinForms contexts on this STA share the thread's marshaling control.
                // Disposing one here also breaks contexts of forms created before this
                // scope (and pending callbacks). The owning UI thread manages its lifetime.
                Control.CheckForIllegalCrossThreadCalls = this.previousCrossThreadChecks;
            }

            Assert.IsTrue(this.exceptions.IsEmpty, "Unhandled WinForms callback exception(s):\n" +
                string.Join("\n", this.exceptions.Select(exception => exception.ToString())));
        }
    }
}
