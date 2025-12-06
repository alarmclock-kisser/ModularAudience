using ManagedCuda;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Cuda
{
	public class CudaExecutioner : IDisposable
	{
		// Fields
		private PrimaryContext CTX;
		private CudaRegister Register;
		private CudaFourier Fourier;
		private CudaCompiler Compiler;

		private CudaKernel? Kernel => this.Compiler.Kernel;

		// Properties



		// Constructor
		public CudaExecutioner(PrimaryContext ctx, CudaRegister register, CudaFourier fourier, CudaCompiler compiler)
		{
			this.CTX = ctx;
			this.Register = register;
			this.Fourier = fourier;
			this.Compiler = compiler;
		}


		// Methods
		public void Dispose()
		{

		}


		// Methods: Time stretching (audio)
		public IntPtr ExecuteTimeStretch(IntPtr indexPointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Kernel '{kernel}' loaded successfully.");
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}
			else
			{
				CudaService.Log($"Localized input memory: {mem.Count} chunks, total length: {mem.TotalLength}, total size: {mem.TotalSize} bytes.");
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr resultPointer = nint.Zero;
			if (mem.ElementType != typeof(float2))
			{
				resultPointer = this.Fourier.PerformFft(indexPointer, keep);
				if (resultPointer == nint.Zero)
				{
					CudaService.Log("Failed to perform FFT on memory.");
					return indexPointer;
				}

				CudaService.Log("Memory transformed to float2 format for time stretch.");	
				transformed = true;
			}

			mem = this.Register[resultPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = this.Register.AllocateGroup<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Allocated output memory: {outMem.Count} chunks, total length: {outMem.TotalLength}, total size: {outMem.TotalSize} bytes.");
			}

			// Exec on every pointer
			try
			{
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) (mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel;

					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;

					k.Run(args);

					CudaService.Log($"Executed kernel on chunk {i + 1}/{mem.Count} with length {mem.Lengths[i]}.");
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel.");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				resultPointer = this.Fourier.PerformIfft(outMem.IndexPointer, keep);
				if (resultPointer == nint.Zero)
				{
					CudaService.Log("Failed to perform inverse FFT on output memory.");
					return indexPointer;
				}
				CudaService.Log("Transformed output memory back to float format after time stretch.");
			}

			outMem = this.Register[resultPointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Output memory after execution: {outMem.Count} chunks, total length: {outMem.TotalLength}, total size: {outMem.TotalSize} bytes.");
			}

			return resultPointer;
		}

		public async Task<IntPtr> ExecuteTimeStretchLinearAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, bool asMany = false, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr indexPointer = mem.IndexPointer;
			if (mem.ElementType != typeof(float2))
			{
				if (asMany)
				{
					indexPointer = await this.Fourier.PerformFftManyAsync(pointer, keep);
				}
				else
				{
					indexPointer = await this.Fourier.PerformFftAsync(pointer, keep);
				}

				transformed = true;
			}

			mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = await this.Register.AllocateGroupAsync<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			// Execute kernel on every pointer per stream
			var stream = this.Register.GetStream();
			if (stream == null)
			{
				CudaService.Log("No stream available for execution.");
				return pointer;
			}

			try
			{
				// Exec on every pointer
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) (mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel;

					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;

					await Task.Run(() => k.RunAsync(stream.Stream, args));
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel linear (async).");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				if (asMany)
				{
					pointer = await this.Fourier.PerformIfftManyAsync(outMem.IndexPointer, keep);
				}
				else
				{
					pointer = await this.Fourier.PerformIfftAsync(outMem.IndexPointer, keep);
				}
			}
			else
			{
				pointer = outMem.IndexPointer;
			}

			outMem = this.Register[pointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return pointer;
			}

			return pointer;
		}

		public async Task<IntPtr> ExecuteTimeStretchInterleavedAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, int maxStreams = 1, bool asMany = false, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr indexPointer = mem.IndexPointer;
			if (mem.ElementType != typeof(float2))
			{
				if (asMany)
				{
					indexPointer = await this.Fourier.PerformFftManyAsync(pointer, keep);
				}
				else
				{
					indexPointer = await this.Fourier.PerformFftAsync(pointer, keep);
				}

				transformed = true;
			}

			mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = await this.Register.AllocateGroupAsync<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			// Execute kernel on every pointer per stream
			var streams = await this.Register.GetManyStreamsAsync(maxStreams);
			if (streams == null || !streams.Any())
			{
				CudaService.Log("No streams available for execution.");
				return pointer;
			}

			try
			{
				ConcurrentDictionary<Task, CudaKernel> tasks = [];
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					int numChunks = mem.Count;
					dim3 blockSize = new(16, 16);
					dim3 gridSize = new(
						(uint) (chunkSize + blockSize.x - 1) / blockSize.x,
						(uint) (numChunks + blockSize.y - 1) / blockSize.y
					);

					var k = this.Kernel;

					k.GridDimensions = gridSize;
					k.BlockDimensions = blockSize;

					tasks[Task.Run(() =>
					{
						var stream = streams.ElementAt(Math.Min(streams.Count() - 1, i));
						if (stream == null)
						{
							CudaService.Log("Invalid stream for execution.");
							return;
						}

						// Launch kernel
						k.RunAsync(stream.Stream, args);
					})] = k;
				}

				await Task.WhenAll(tasks.Keys);
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel interleaved (async).");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				if (asMany)
				{
					pointer = await this.Fourier.PerformIfftManyAsync(outMem.IndexPointer, keep);
				}
				else
				{
					pointer = await this.Fourier.PerformIfftAsync(outMem.IndexPointer, keep);
				}
			}
			else
			{
				pointer = outMem.IndexPointer;
			}

			outMem = this.Register[pointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return pointer;
			}

			return pointer;
		}


	}
}
