using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Silk.NET.Vulkan;

namespace ModularAudience.Gpu
{
    unsafe
    public class GpuService : IDisposable
    {
        private readonly Vk _vk;
        private readonly Instance _instance;
        private List<PhysicalDevice> _physicalDevices = [];
        private VulkanCompiler _vulkanCompiler;
        private VulkanRegister _vulkanRegister;
        private Device? _logicalDevice;
        private bool _disposed = false;

        public GpuService()
        {
            this._vk = Vk.GetApi();
            this._instance = new Instance();
            InitializeLogicalDevice();
            _vulkanCompiler = new VulkanCompiler(this._vk, this._instance, _logicalDevice);
            _vulkanRegister = new VulkanRegister(this._vk, this._instance, _logicalDevice);
        }

        private void InitializeLogicalDevice()
        {
            // PhysicalDevice (by index) auswählen und daraus LogicalDevice erstellen
            var physicalDevice = _physicalDevices.Count > 0 ? _physicalDevices[0] : null;
            if (physicalDevice == null)
            {
                throw new Exception("No physical device available for logical device creation.");
            }
            // Silk.NET verwendet Device als Handle. Die konkrete Vk.CreateDevice-Initialisierung folgt,
            // sobald Queue-Familie und DeviceCreateInfo festgelegt sind.
            _logicalDevice = default;
        }

        public List<PhysicalDevice> GetPhysicalDevices()
        {
            // Query and return the list of physical devices
            uint deviceCount = 0;
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);
            if (deviceCount == 0)
            {
                throw new Exception("No physical devices found.");
            }
            PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);
            _physicalDevices = new List<PhysicalDevice>(devices);
            return _physicalDevices;
        }

        public VulkanCompiler VulkanCompiler => _vulkanCompiler;
        public VulkanRegister VulkanRegister => _vulkanRegister;
        public Device? LogicalDevice => _logicalDevice;

        public void Dispose()
        {
            if (_disposed) return;
            _vulkanCompiler?.Dispose();
            _vulkanRegister?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public struct PhysicalDevice
    {
        public nint Handle;
        public override string ToString();
    }
