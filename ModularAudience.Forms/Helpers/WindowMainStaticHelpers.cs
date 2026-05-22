using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.ComponentModel;

namespace ModularAudience.Forms.Helpers
{
    internal static class WindowMainStaticHelpers
    {
        internal static void InvokeIfRequired(WindowMain? instance, Action action)
        {
            if (instance == null || instance.IsDisposed)
            {
                return;
            }

            if (instance.InvokeRequired)
            {
                try { instance.BeginInvoke(action); } catch { }
            }
            else
            {
                try { action(); } catch { }
            }
        }

        internal static async Task InvokeIfRequiredAsync(WindowMain? instance, Func<Task> asyncAction)
        {
            if (instance == null || instance.IsDisposed)
            {
                return;
            }

            try
            {
                if (instance.InvokeRequired)
                {
                    await (Task) instance.Invoke(asyncAction)!;
                }
                else
                {
                    await asyncAction();
                }
            }
            catch { }
        }

        internal static void UnselectAll(BindingList<AudioCollectionView> views, AudioCollectionView? except = null)
        {
            views.Where(cv => cv != except).ToList().ForEach(cv => cv.UnselectAll());
        }

        internal static void RefreshAllCollectionViews(BindingList<AudioCollectionView> views, Action? onDone = null)
        {
            foreach (var cv in views)
            {
                cv.RefreshList();
            }

            onDone?.Invoke();
        }

        internal static int GetCollectionNumber(BindingList<AudioCollectionView> views, AudioCollectionView view)
        {
            try
            {
                var text = view.Text;
                int idx = text.LastIndexOf('#');
                if (idx >= 0 && idx + 3 <= text.Length)
                {
                    var numStr = text.Substring(idx + 1, 2);
                    if (int.TryParse(numStr, out int num))
                    {
                        return num;
                    }
                }
            }
            catch { }

            int index = views.ToList().IndexOf(view);
            return index >= 0 ? index + 1 : 1;
        }

        internal static void UpdateCollectionTag(
            Dictionary<Guid, int> tags,
            BindingList<AudioCollectionView> views,
            AudioObj audio,
            AudioCollectionView targetView)
        {
            if (audio == null || targetView == null)
            {
                return;
            }

            int num = GetCollectionNumber(views, targetView);
            tags[audio.Id] = num;
        }

        internal static string? TryGetRandomResourceFile(HashSet<string> allowedImportExtensions, Random resourceRandom)
        {
            var candidates = new List<string>();
            foreach (var root in EnumerateResourceRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    candidates.AddRange(Directory
                        .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(f => allowedImportExtensions.Contains(Path.GetExtension(f))));
                }
                catch
                {
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            lock (resourceRandom)
            {
                return candidates[resourceRandom.Next(candidates.Count)];
            }
        }

        internal static IEnumerable<string> EnumerateResourceRoots()
        {
            DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "Resources");
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }

                current = current.Parent;
            }
        }
    }
}
