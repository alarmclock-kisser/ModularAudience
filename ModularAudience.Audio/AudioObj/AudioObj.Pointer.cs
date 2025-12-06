using System;
using System.Collections.Generic;
using System.Text;

namespace ModularAudience.Audio
{
	public partial class AudioObj
	{
		public bool OnHost => this.Data.LongLength > 0 && this.Pointer == IntPtr.Zero;
		public bool OnDevice => this.Pointer != IntPtr.Zero;
		public IntPtr Pointer { get; set; } = IntPtr.Zero;
		public string Form { get; set; } = "f";
		public bool IsProcessing { get; set; } = false;

	}
}
