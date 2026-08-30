using ManagedCuda;
using ManagedCuda.BasicTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ModularAudience.Cuda
{
    public class CudaRegister : IDisposable
    {
        private ConcurrentDictionary<Guid, CudaMem> memory = [];
        public IReadOnlyList<CudaMem> Memory => this.memory.Values.ToList();

        public ConcurrentDictionary<CudaStream, int> streams = [];

        private PrimaryContext CTX;


        // Properties
        public long TotalAllocated => this.memory.Values.Sum(m => m.TotalSize);
        public int RegisteredMemoryObjects => this.memory.Count;
        public int ThreadsActive => this.streams.Count(s => s.Value > 0);
        public int ThreadsIdle => this.streams.Count(s => s.Value <= 0);
        public int MaxThreads => this.CTX.GetDeviceInfo().MaxThreadsPerMultiProcessor;


        // Constructor
        public CudaRegister(PrimaryContext ctx)
        {
            this.CTX = ctx;

            this.CTX.SetCurrent();
        }

        // Enumerable
        public CudaMem? this[Guid id]
        {
            get
            {
                if (this.memory.TryGetValue(id, out CudaMem? mem))
                {
                    return mem;
                }

                CudaService.Log($"CudaMem with ID {id} not found in register.");
                return null;
            }
        }

        public CudaMem? this[IntPtr indexPointer]
        {
            get
            {
                if (this.memory.Values.Any(m => m.IndexPointer == indexPointer))
                {
                    return this.memory.Values.FirstOrDefault(m => m.IndexPointer == indexPointer);
                }

                CudaService.Log($"CudaMem with IndexPointer {indexPointer} not found in register.");
                return null;
            }
        }


        // Methods: Dispose & Free
        public void Dispose()
        {
            CudaService.Log($"Disposing CudaRegister with {this.RegisteredMemoryObjects} registered memory objects and {this.streams.Count} streams ...");

            // Free every CudaMem object
            foreach (var mem in this.memory.Values)
            {
                this.FreeMemory(mem);
            }

            this.memory.Clear();

            // Dispose all streams
            foreach (var stream in this.streams.Keys)
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception ex)
                {
                    CudaService.Log($"Error disposing stream: {ex.Message}");
                }
            }

            this.streams.Clear();

            CudaService.Log($"CudaRegister disposed.");

            GC.SuppressFinalize(this);
        }

        public long FreeMemory(CudaMem mem)
        {
            long freed = mem.TotalSize;

            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.CTX.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            if (this.memory.TryRemove(mem.Id, out _))
            {
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }

        public long FreeMemory(IntPtr indexPointer)
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null)
            {
                return 0;
            }

            long freed = mem.TotalSize;

            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.CTX.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    CudaService.Log(ex, "Error freeing memory");
                }
            }

            if (this.memory.TryRemove(mem.Id, out _))
            {
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }

        public long FreeMemory(Guid id)
        {
            CudaMem? mem = this[id];
            if (mem == null)
            {
                return 0;
            }

            long freed = mem.TotalSize;

            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.CTX.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    CudaService.Log(ex, "Error freeing memory");
                }
            }

            if (this.memory.TryRemove(mem.Id, out _))
            {
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }


        // Methods: Stream(s)
        internal ulong? CreateStream()
        {
            Guid id = Guid.Empty;

            try
            {
                this.CTX.SetCurrent();
                CudaStream stream = new();
                stream.Synchronize();

                id = Guid.NewGuid();
                if (this.streams.TryAdd(stream, 0))
                {
                    return stream.ID;
                }
                else
                {
                    stream.Dispose();
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error creating stream");
                return null;
            }
        }

        public CudaStream? GetStream(ulong? id = null)
        {
            int engines = this.CTX.GetDeviceInfo().AsyncEngineCount;
            int streams = this.streams.Count;

            ulong? streamId = null;
            CudaStream? stream = null;

            // Finds the stream by ID if provided, or returns stream with lowest value, or creates a new stream if no ID is provided
            if (id.HasValue)
            {
                stream = this.streams.Keys.FirstOrDefault(s => s.ID == id.Value);
            }
            else
            {
                if (streams < engines)
                {
                    streamId = this.CreateStream();
                    if (streamId.HasValue)
                    {
                        stream = this.streams.Keys.FirstOrDefault(s => s.ID == streamId.Value);
                    }
                }
                else
                {
                    // Select stream with the lowest value
                    stream = this.streams.Keys.OrderBy(s => this.streams[s]).FirstOrDefault();
                }
            }

            if (stream == null)
            {
                CudaService.Log("No available stream found or created.", $"{(id.HasValue ? $"ID was {id}" : "")}");
            }

            return stream;
        }

        public IEnumerable<CudaStream>? GetManyStreams(int maxCount = 0, IEnumerable<ulong>? ids = null)
        {
            if (maxCount <= 0)
            {
                maxCount = this.MaxThreads - this.streams.Count();
            }
            if (maxCount <= 0 || this.CTX == null)
            {
                return null;
            }

            CudaStream[] created = new CudaStream[maxCount];
            var results = created.Select((s, i) =>
            {
                CudaStream? stream = null;
                if (ids != null && ids.Any())
                {
                    stream = this.GetStream(ids.ElementAt(i));
                }
                else
                {
                    stream = this.GetStream();
                }
                if (stream == null)
                {
                    return null;
                }
                created[i] = stream;
                return stream;
            });

            foreach (var s in results)
            {
                if (s != null && !this.streams.ContainsKey(s))
                {
                    if (this.streams.TryAdd(s, 0))
                    {
                        s.Synchronize();
                    }
                    else
                    {
                        s.Dispose();
                    }
                }
            }

            if (created.Any(s => s == null))
            {
                CudaService.Log("Some streams could not be created or retrieved.");
                return null;
            }

            return created;
        }

        public async Task<IEnumerable<CudaStream>?> GetManyStreamsAsync(int maxCount = 0, IEnumerable<ulong>? ids = null)
        {
            if (maxCount <= 0)
            {
                maxCount = this.MaxThreads - this.streams.Count();
            }
            if (maxCount <= 0 || this.CTX == null)
            {
                return null;
            }

            CudaStream[] created = new CudaStream[maxCount];
            var results = created.Select((s, i) =>
            {
                CudaStream? stream = null;
                if (ids != null && ids.Any())
                {
                    stream = this.GetStream(ids.ElementAt(i));
                }
                else
                {
                    stream = this.GetStream();
                }

                if (stream == null)
                {
                    return null;
                }

                created[i] = stream;
                return stream;
            });

            int added = 0;
            foreach (var s in results)
            {
                if (s != null && !this.streams.ContainsKey(s))
                {
                    if (this.streams.TryAdd(s, 0))
                    {
                        await Task.Run(s.Synchronize);
                        added++;
                    }
                    else
                    {
                        s.Dispose();
                    }
                }
            }

            if (added == 0)
            {
                CudaService.Log("No streams could be created or retrieved.");
                return null;
            }

            return created;
        }


        // Methods: Allocate
        public CudaMem? AllocateSingle<T>(IntPtr length) where T : unmanaged
        {
            if (length <= 0)
            {
                return null;
            }

            try
            {
                CudaDeviceVariable<T> devVariable = new((long) length);
                var pointer = devVariable.DevicePointer;
                CudaMem mem = new(pointer, length, typeof(T));
                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    CudaService.Log($"Failed to allocate memory for {typeof(T).Name} of length {length}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, $"Error allocating single memory");
                return null;
            }
        }

        public async Task<CudaMem?> AllocateSingleAsync<T>(IntPtr length) where T : unmanaged
        {
            if (length <= 0)
            {
                return null;
            }

            // this.CTX.SetCurrent();

            try
            {
                var stream = this.GetStream();
                if (stream == null)
                {
                    return null;
                }

                CudaDeviceVariable<T> devVariable = new((long) length, stream);
                var pointer = devVariable.DevicePointer;

                CudaMem mem = new(pointer, length, typeof(T));

                await Task.Run(stream.Synchronize);

                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    CudaService.Log($"Failed to allocate memory for {typeof(T).Name} of length {length}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, $"Error allocating single memory (async)");
                return null;
            }
        }

        public CudaMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (lengths.LongLength <= 0 || lengths.Any(l => l <= 0))
            {
                return null;
            }

            try
            {
                CudaDeviceVariable<T>[] devVariables = lengths.Select(l => new CudaDeviceVariable<T>((long) l)).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();
                CudaMem mem = new(pointers, lengths, typeof(T));
                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }
                    mem.Dispose();
                    CudaService.Log($"Failed to allocate grouped memory for {typeof(T).Name} with lengths {lengths.LongLength + "x " + lengths.FirstOrDefault()}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error allocating grouped memory");
                return null;
            }
        }

        public async Task<CudaMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (lengths.LongLength <= 0 || lengths.Any(l => l <= 0))
            {
                return null;
            }

            var stream = this.GetStream();
            if (stream == null)
            {
                return null;
            }

            try
            {
                CudaDeviceVariable<T>[] devVariables = lengths.Select(l => new CudaDeviceVariable<T>((long) l, stream)).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                CudaMem mem = new(pointers, lengths, typeof(T));

                await Task.Run(stream.Synchronize);

                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }
                    mem.Dispose();
                    CudaService.Log($"Failed to allocate grouped memory for {typeof(T).Name} with lengths {lengths.LongLength + "x " + lengths.FirstOrDefault()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error allocating grouped memory (async)");
                return null;
            }
        }


        // Methods: Push data / -chunks
        public CudaMem? PushData<T>(IEnumerable<T> data) where T : unmanaged
        {
            if (data == null || !data.Any())
            {
                return null;
            }

            try
            {
                IntPtr length = (nint) data.LongCount();
                CudaDeviceVariable<T> devVariable = new(length);
                var pointer = devVariable.DevicePointer;

                this.CTX.CopyToDevice(devVariable.DevicePointer, data.ToArray());

                CudaMem mem = new(pointer, length, typeof(T));

                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    CudaService.Log($"Failed to push data for {typeof(T).Name} of length {length}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pushing data");
                return null;
            }
        }

        public CudaMem? PushChunks<T>(IEnumerable<IEnumerable<T>> chunks) where T : unmanaged
        {
            if (chunks == null || !chunks.Any())
            {
                return null;
            }

            try
            {
                IntPtr[] lengths = chunks.Select(chunk => (nint) chunk.LongCount()).ToArray();
                CudaDeviceVariable<T>[] devVariables = chunks.Select(chunk => new CudaDeviceVariable<T>((nint) chunk.LongCount())).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                for (int i = 0; i < chunks.Count(); i++)
                {
                    this.CTX.CopyToDevice(devVariables[i].DevicePointer, chunks.ElementAt(i).ToArray());
                }

                CudaMem mem = new(pointers, lengths, typeof(T));

                if (this.memory.TryAdd(mem.Id, mem))
                {
                    return mem;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }
                    mem.Dispose();
                    CudaService.Log($"Failed to push chunks for {typeof(T).Name} with lengths {lengths.LongLength + "x " + lengths.FirstOrDefault()}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pushing chunks");
                return null;
            }
        }

        public async Task<CudaMem?> PushDataAsync<T>(IEnumerable<T> data, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = null;

            var stream = this.GetStream(id);
            if (stream == null)
            {
                return null;
            }

            this.streams[stream]++;

            try
            {
                IntPtr length = (nint) data.LongCount();
                CudaDeviceVariable<T> devVariable = new(length, stream);
                var pointer = devVariable.DevicePointer;

                devVariable.AsyncCopyToDevice(data.ToArray(), stream);
                await Task.Run(stream.Synchronize);

                mem = new(pointer, length, typeof(T));
                if (!this.memory.TryAdd(mem.Id, mem))
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    CudaService.Log($"Failed to push data for {typeof(T).Name} of length {length}.");
                    mem = null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, $"Error pushing data (async)");
            }
            finally
            {
                this.streams[stream]--;
                GC.Collect();
            }

            return mem;
        }

        public async Task<CudaMem?> PushChunksAsync<T>(IEnumerable<IEnumerable<T>> chunks, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = null;
            var stream = this.GetStream(id);
            if (stream == null)
            {
                return null;
            }
            this.streams[stream]++;

            try
            {
                IntPtr[] lengths = chunks.Select(chunk => (nint) chunk.LongCount()).ToArray();
                CudaDeviceVariable<T>[] devVariables = chunks.Select(chunk => new CudaDeviceVariable<T>((nint) chunk.LongCount(), stream)).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                for (int i = 0; i < chunks.Count(); i++)
                {
                    devVariables[i].AsyncCopyToDevice(chunks.ElementAt(i).ToArray(), stream);
                }

                await Task.Run(stream.Synchronize);

                mem = new(pointers, lengths, typeof(T));
                if (!this.memory.TryAdd(mem.Id, mem))
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }

                    mem.Dispose();
                    CudaService.Log($"Failed to push chunks for {typeof(T).Name} with lengths {lengths.LongLength + "x " + lengths.FirstOrDefault()}.");
                    mem = null;
                }
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, $"Error pushing chunks (async)");
            }
            finally
            {
                this.streams[stream]--;
                GC.Collect();
            }

            return mem;
        }


        // Methods: Pull data / -chunks
        public T[] PullData<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];

            if (mem == null || mem.Pointers.Length == 0 || mem.Lengths.Length == 0)
            {
                return [];
            }

            try
            {
                CUdeviceptr devicePointer = new(mem.IndexPointer);
                CudaDeviceVariable<T> devVariable = new(devicePointer, mem.IndexLength);
                T[] data = new T[mem.IndexLength];

                this.CTX.CopyToHost(data, devVariable.DevicePointer);

                // this.CTX.Synchronize();

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return data;
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pulling data");
                return [];
            }
        }

        public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];

            if (mem == null || mem.Pointers.Length == 0 || mem.Lengths.Length == 0)
            {
                return [];
            }

            try
            {
                List<T[]> chunks = [];
                CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
                CudaDeviceVariable<T>[] devVariables = devicePointers.Select((ptr, i) => new CudaDeviceVariable<T>(ptr, mem.Lengths[i])).ToArray();

                for (int i = 0; i < devVariables.Length; i++)
                {
                    T[] chunkData = new T[mem.Lengths[i]];
                    this.CTX.CopyToHost(chunkData, devVariables[i].DevicePointer);
                    chunks.Add(chunkData);
                }

                this.CTX.Synchronize();

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return chunks;
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pulling chunks");
                return [];
            }
        }

        public async Task<T[]> PullDataAsync<T>(IntPtr indexPointer, bool keep = false, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null || mem.Count <= 0)
            {
                return [];
            }

            try
            {
                T[] data = new T[mem.Count];
                var stream = this.GetStream(id);
                if (stream == null)
                {
                    return data;
                }
                this.streams[stream]++;

                // this.CTX.SetCurrent();

                CUdeviceptr devicePtr = new(mem.IndexPointer);

                // Native asynchroner Transfer: Device → Host
                int byteSize = data.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
                unsafe
                {
                    fixed (T* pData = data)
                    {
                        var res = DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
                            new CUdeviceptr((IntPtr) pData),
                            devicePtr,
                            (SizeT) byteSize,
                            stream.Stream
                        );
                        if (res != CUResult.Success)
                        {
                            throw new CudaException(res);
                        }
                    }
                }

                await Task.Run(stream.Synchronize);

                this.streams[stream]--;

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return data;
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pulling data (async)");
                return [];
            }
            finally
            {
                GC.Collect();
            }
        }

        public async Task<List<T[]>> PullChunksAsync<T>(IntPtr indexPointer, bool keep = false, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null || mem.Count <= 0)
            {
                return [];
            }

            try
            {
                List<T[]> chunks = [];
                var stream = this.GetStream(id);
                if (stream == null)
                {
                    return chunks;
                }
                this.streams[stream]++;

                // this.CTX.SetCurrent();

                CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
                for (int i = 0; i < devicePointers.Length; i++)
                {
                    T[] data = new T[mem.Lengths[i]];
                    CUdeviceptr devicePtr = devicePointers[i];

                    // Native asynchroner Transfer: Device → Host
                    int byteSize = data.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
                    unsafe
                    {
                        fixed (T* pData = data)
                        {
                            var res = DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
                                new CUdeviceptr((IntPtr) pData),
                                devicePtr,
                                (SizeT) byteSize,
                                stream.Stream
                            );
                            if (res != CUResult.Success)
                            {
                                throw new CudaException(res);
                            }
                        }
                    }

                    chunks.Add(data);
                }

                await Task.Run(stream.Synchronize);

                this.streams[stream]--;

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return chunks;
            }
            catch (Exception ex)
            {
                CudaService.Log(ex, "Error pulling chunks (async)");
                return [];
            }
            finally
            {
                GC.Collect();
            }
        }

    }
}
