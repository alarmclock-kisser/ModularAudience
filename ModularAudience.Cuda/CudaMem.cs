using ManagedCuda.BasicTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Cuda
{
    public class CudaMem : IDisposable
    {
        public Guid Id { get; private set; } = Guid.NewGuid();

        public CUdeviceptr[] DevicePointers { get; private set; } = [];
        public IntPtr[] Pointers { get; private set; } = [];
        public IntPtr[] Lengths { get; private set; } = [];

        public IntPtr IndexPointer { get; private set; } = nint.Zero;
        public IntPtr IndexLength { get; private set; } = nint.Zero;

        public Type ElementType { get; private set; } = typeof(void);
        public int ElementSize { get; private set; } = 0;

        public int Count { get; private set; } = 0;
        public long TotalLength { get; private set; } = 0;
        public long TotalSize { get; private set; } = 0;

        public string Message { get; set; } = string.Empty;


        // Enumerable
        public CUdeviceptr? this[IntPtr pointer]
        {
            get
            {
                int index = Array.IndexOf(this.Pointers, pointer);
                if (index >= 0 && index < this.DevicePointers.Length)
                {
                    return this.DevicePointers[index];
                }

                return null;
            }
        }


        // Constructors
        public CudaMem(CUdeviceptr pointer, IntPtr length, Type type)
        {
            this.DevicePointers = [pointer];
            this.Pointers = [pointer.Pointer];
            this.Lengths = [length];
            this.ElementType = type;

            this.UpdateProperties();
        }

        public CudaMem(CUdeviceptr[] pointers, IntPtr[] lengths, Type type)
        {
            if (pointers.Length != lengths.Length || pointers.Length <= 0 || lengths.Length <= 0)
            {
                this.Dispose();
            }

            this.DevicePointers = pointers;
            this.Pointers = pointers.Select(ptr => (nint) ptr.Pointer).ToArray();
            this.Lengths = lengths;
            this.ElementType = type;

            this.UpdateProperties();
        }




        // Methods
        public void Dispose()
        {
            this.Pointers = [];
            this.Lengths = [];
            this.ElementType = typeof(void);
            this.ElementSize = 0;
            this.Count = 0;
            this.TotalLength = 0;
            this.Message = string.Empty;

            GC.SuppressFinalize(this);
        }

        private void UpdateProperties()
        {
            this.ElementSize = System.Runtime.InteropServices.Marshal.SizeOf(this.ElementType);
            this.Count = this.Pointers.Length;
            this.TotalLength = this.Lengths.Sum(len => len.ToInt64());
            this.TotalSize = this.TotalLength * this.ElementSize;
            this.IndexPointer = this.Pointers.FirstOrDefault(nint.Zero);
            this.IndexLength = this.Lengths.FirstOrDefault(nint.Zero);
        }

    }
}
