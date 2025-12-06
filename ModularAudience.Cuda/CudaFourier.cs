using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.CudaFFT;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Cuda
{
	public class CudaFourier : IDisposable
	{


		private PrimaryContext CTX;
		private CudaRegister Register;


		// Constructor
		public CudaFourier(PrimaryContext ctx, CudaRegister register)
		{
			this.CTX = ctx;
			this.Register = register;
		}


		// Method: Dispose
		public void Dispose()
		{

		}


		// Methods: FFT (Fourier Transform forward)
		public IntPtr PerformFft(IntPtr indexPointer, bool keep = false)
		{
			// Check initialized & input pointer
			if (this.Register == null || this.CTX == null || indexPointer == nint.Zero)
			{
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Allocate output memory
			var outputMem = this.Register.AllocateGroup<float2>(mem.Lengths.Select(l => l).ToArray());
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}

			// Use Context instead of stream(s)	
			// this.CTX.Synchronize();

			// Check if all lengths are the same (else create plan for each length in loop)
			try
			{
				if (mem.Lengths.Distinct().Count() == 1)
				{
					int nx = (int) mem.IndexLength.ToInt64();
					cufftType fftType = cufftType.R2C;
					int batch = 1;

					CudaFFTPlan1D plan = new(nx, fftType, batch);
					for (int i = 0; i < mem.Count; i++)
					{
						CUdeviceptr inPtr = new(mem.Pointers[i]);
						CUdeviceptr outPtr = new(outputMem.Pointers[i]);
						plan.Exec(inPtr, outPtr);
						outputMem.Pointers[i] = outPtr.Pointer;
					}
					plan.Dispose();
				}
				else
				{
					int[] nx = mem.Lengths.Select(l => (int) l.ToInt64()).ToArray();
					cufftType fftType = cufftType.R2C;
					int batch = 1;

					for (int i = 0; i < mem.Count; i++)
					{
						CudaFFTPlan1D plan = new(nx[i], fftType, batch);
						CUdeviceptr inPtr = new(mem.Pointers[i]);
						CUdeviceptr outPtr = new(outputMem.Pointers[i]);
						plan.Exec(inPtr, outPtr);
						outputMem.Pointers[i] = outPtr.Pointer;
						plan.Dispose();
					}
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during FFT execution.");
				this.Register.FreeMemory(outputMem);
				return indexPointer;
			}

			// Check output memory
			if (outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("FFT execution failed, result pointer is null.");
				this.Register.FreeMemory(outputMem);
				return indexPointer;
			}

			// Synchronize context
			// this.CTX.Synchronize();

			// Optionally free input memory
			if (!keep)
			{
				this.Register.FreeMemory(indexPointer);
			}

			return outputMem.IndexPointer;
		}

		public async Task<IntPtr> PerformFftAsync(IntPtr pointer, bool keep = false)
		{
			if (this.Register == null || this.CTX == null || pointer == nint.Zero)
			{
				return nint.Zero;
			}

			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			var outputMem = await this.Register.AllocateGroupAsync<float2>(mem.Lengths.Select(l => l).ToArray());
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			try
			{
				var stream = this.Register.GetStream();
				if (stream == null)
				{
					this.Register.FreeMemory(outputMem);
					return pointer;
				}

				int nx = (int) mem.IndexLength.ToInt64();
				cufftType fftType = cufftType.R2C;
				int batch = 1;
				CudaFFTPlan1D plan = new(nx, fftType, batch, stream.Stream);

				for (int i = 0; i < mem.Count; i++)
				{
					CUdeviceptr inPtr = new(mem.Pointers[i]);
					CUdeviceptr outPtr = new(outputMem.Pointers[i]);
					plan.Exec(inPtr, outPtr);
					outputMem.Pointers[i] = outPtr.Pointer;
				}

				await Task.Run(stream.Synchronize);
				plan.Dispose();

			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during FFT execution.");
				this.Register.FreeMemory(outputMem);
				return pointer;
			}

			if (outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("FFT execution failed, result pointer is null.");
				this.Register.FreeMemory(outputMem);
				return pointer;
			}

			if (!keep)
			{
				this.Register.FreeMemory(pointer);
			}

			return outputMem.IndexPointer;
		}

		public async Task<IntPtr> PerformFftManyAsync(IntPtr indexPointer, bool keep = false)
		{
			if (this.Register == null || this.CTX == null || indexPointer == nint.Zero)
			{
				return nint.Zero;
			}

			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			IntPtr[] lengths = mem.Lengths;

			var outputMem = await this.Register.AllocateGroupAsync<float2>(lengths);
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}

			try
			{
				var stream = this.Register.GetStream();
				if (stream == null)
				{
					this.Register.FreeMemory(outputMem);
					return indexPointer;
				}

				int rank = 1;
				cufftType fftType = cufftType.R2C;
				CudaFFTPlanMany plan = new(rank, lengths.Select(l => (int)l.ToInt64()).ToArray(), mem.Count, fftType, stream.Stream);

				for (int i = 0; i < mem.Count; i++)
				{
					CUdeviceptr inPtr = new(mem.Pointers[i]);
					CUdeviceptr outPtr = new(outputMem.Pointers[i]);

					plan.Exec(inPtr, outPtr);

					outputMem.Pointers[i] = outPtr.Pointer;
				}

				if (outputMem.IndexPointer == nint.Zero)
				{
					CudaService.Log("FFT-many execution failed, result pointer is null.");
					this.Register.FreeMemory(outputMem);
					return indexPointer;
				}

				await Task.Run(stream.Synchronize);

				if (!keep)
				{
					this.Register.FreeMemory(indexPointer);
				}

				plan.Dispose();
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during FFT-many execution.");
				return indexPointer;
			}

			return outputMem.IndexPointer;
		}


		// Methods: IFFT (Fourier Transform inverse)
		public IntPtr PerformIfft(IntPtr indexPointer, bool keep = false)
		{
			// Check initialized & input pointer
			if (this.Register == null || this.CTX == null || indexPointer == nint.Zero)
			{
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Allocate output memory
			var outputMem = this.Register.AllocateGroup<float>(mem.Lengths.Select(l => l).ToArray());
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}

			// Use Context instead of stream(s)	
			// this.CTX.Synchronize();

			// Check if all lengths are the same (else create plan for each length in loop)
			try
			{
				if (mem.Lengths.Distinct().Count() == 1)
				{
					int nx = (int) mem.IndexLength.ToInt64();
					cufftType fftType = cufftType.C2R;
					int batch = 1;

					CudaFFTPlan1D plan = new(nx, fftType, batch);
					for (int i = 0; i < mem.Count; i++)
					{
						CUdeviceptr inPtr = new(mem.Pointers[i]);
						CUdeviceptr outPtr = new(outputMem.Pointers[i]);
						plan.Exec(inPtr, outPtr);
						outputMem.Pointers[i] = outPtr.Pointer;
					}
					plan.Dispose();
				}
				else
				{
					int[] nx = mem.Lengths.Select(l => (int) l.ToInt64()).ToArray();
					cufftType fftType = cufftType.C2R;
					int batch = 1;

					for (int i = 0; i < mem.Count; i++)
					{
						CudaFFTPlan1D plan = new(nx[i], fftType, batch);
						CUdeviceptr inPtr = new(mem.Pointers[i]);
						CUdeviceptr outPtr = new(outputMem.Pointers[i]);
						plan.Exec(inPtr, outPtr);
						outputMem.Pointers[i] = outPtr.Pointer;
						plan.Dispose();
					}
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during IFFT execution.");
				this.Register.FreeMemory(outputMem);
				return indexPointer;
			}

			// Check output memory
			if (outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("FFT execution failed, result pointer is null.");
				this.Register.FreeMemory(outputMem);
				return indexPointer;
			}

			// Synchronize context
			// this.CTX.Synchronize();

			// Optionally free input memory
			if (!keep)
			{
				this.Register.FreeMemory(indexPointer);
			}

			return outputMem.IndexPointer;
		}

		public async Task<IntPtr> PerformIfftAsync(IntPtr pointer, bool keep = false)
		{
			if (this.Register == null || this.CTX == null || pointer == nint.Zero)
			{
				return nint.Zero;
			}

			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			var outputMem = await this.Register.AllocateGroupAsync<float>(mem.Lengths.Select(l => l).ToArray());
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			try
			{
				var stream = this.Register.GetStream();
				if (stream == null)
				{
					this.Register.FreeMemory(outputMem);
					return pointer;
				}

				int nx = (int) mem.IndexLength.ToInt64();
				cufftType fftType = cufftType.C2R;
				int batch = 1;
				CudaFFTPlan1D plan = new(nx, fftType, batch, stream.Stream);

				for (int i = 0; i < mem.Count; i++)
				{
					CUdeviceptr inPtr = new(mem.Pointers[i]);
					CUdeviceptr outPtr = new(outputMem.Pointers[i]);
					plan.Exec(inPtr, outPtr);
					outputMem.Pointers[i] = outPtr.Pointer;
				}

				await Task.Run(stream.Synchronize);
				plan.Dispose();
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during IFFT execution.");
				this.Register.FreeMemory(outputMem);
				return pointer;
			}

			if (outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("IFFT execution failed, result pointer is null.");
				this.Register.FreeMemory(outputMem);
				return pointer;
			}

			if (!keep)
			{
				this.Register.FreeMemory(pointer);
			}

			return outputMem.IndexPointer;
		}

		public async Task<IntPtr> PerformIfftManyAsync(IntPtr indexPointer, bool keep = false)
		{
			if (this.Register == null || this.CTX == null || indexPointer == nint.Zero)
			{
				return nint.Zero;
			}

			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			IntPtr[] lengths = mem.Lengths;

			var outputMem = await this.Register.AllocateGroupAsync<float>(lengths);
			if (outputMem == null || outputMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}

			try
			{
				var stream = this.Register.GetStream();
				if (stream == null)
				{
					this.Register.FreeMemory(outputMem);
					return indexPointer;
				}

				int rank = 1;
				cufftType fftType = cufftType.C2R;
				CudaFFTPlanMany plan = new(rank, lengths.Select(l => (int) l.ToInt64()).ToArray(), mem.Count, fftType, stream.Stream);

				for (int i = 0; i < mem.Count; i++)
				{
					CUdeviceptr inPtr = new(mem.Pointers[i]);
					CUdeviceptr outPtr = new(outputMem.Pointers[i]);

					plan.Exec(inPtr, outPtr);

					outputMem.Pointers[i] = outPtr.Pointer;
				}

				if (outputMem.IndexPointer == nint.Zero)
				{
					CudaService.Log("IFFT-many execution failed, result pointer is null.");
					this.Register.FreeMemory(outputMem);
					return indexPointer;
				}

				await Task.Run(stream.Synchronize);

				if (!keep)
				{
					this.Register.FreeMemory(indexPointer);
				}

				plan.Dispose();
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error during IFFT-many execution.");
				return indexPointer;
			}

			return outputMem.IndexPointer;
		}

	}
}
