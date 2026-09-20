using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// A byte-array resource store backed by an in-memory dictionary, so a
    /// synthesized sound needs no file on disk and no embedded resource.
    ///
    /// Written for the gameplay hitsounds and shared with the menu's own
    /// synthesized sounds — both generate their audio at startup and hand it
    /// straight to osu.Framework's sample store.
    /// </summary>
    internal sealed class InMemorySampleStore : IResourceStore<byte[]>
    {
        private readonly Dictionary<string, byte[]> data;

        public InMemorySampleStore(Dictionary<string, byte[]> data)
        {
            this.data = data;
        }

        public byte[] Get(string name) => data.TryGetValue(name, out var bytes) ? bytes : null!;

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Get(name));

        public Stream GetStream(string name) =>
            data.TryGetValue(name, out var bytes) ? new MemoryStream(bytes) : null!;

        public IEnumerable<string> GetAvailableResources() => data.Keys;

        public void Dispose()
        {
        }
    }
}
