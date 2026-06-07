using ModularAudience.Audio;
using ModularAudience.Cuda;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
	public partial class CudaFunctions : Form
	{
		private readonly CudaService _cuda = new();
		internal readonly AudioCollection AudioC = new();


		private int _chunkSize => (int) this.numericUpDown_chunkSize.Value;
		private float _overlap => (float) this.numericUpDown_overlap.Value;
		private IntPtr? _selectedIndexPointer => this.GetSelectedIndexPointer();
		private string? _selectedKernelName => this.comboBox_kernels.SelectedItem as string;


		private Dictionary<string, NumericUpDown> _argumentControls => this.panel_arguments.Controls.OfType<NumericUpDown>()
			.ToDictionary(c => c.Name.Split("_").Last(), c => c);
		internal Dictionary<string, Type> ArgumentTypes => this._cuda.GetKernelArgumentTypes(this._selectedKernelName) ?? [];
		internal Dictionary<string, object> ArgumentValues => this._argumentControls.ToDictionary(kvp => kvp.Key, kvp => Convert.ChangeType(kvp.Value.Value, this.ArgumentTypes[kvp.Key]));




		public CudaFunctions()
		{
			this.InitializeComponent();
			this.StartPosition = FormStartPosition.Manual;
			this.Location = WindowsScreenHelper.GetCenterStartingPoint(null, WindowMain.CurrentScreenId);

			// Preserve originals when importing from other views
			this.AudioC.KeepOriginal = true;

			this.ComboBox_FillDevices(this.comboBox_devices);
			this.ComboBox_FillKernels(this.comboBox_kernels);

			this.listBox_log.Items.Clear();
			this.listBox_log.DataSource = CudaService.LogEntries;
			CudaService.LogEntries.ListChanged += (s, e) =>
			{
				this.listBox_log.TopIndex = this.listBox_log.Items.Count - 1;
			};

			this.listBox_log.DoubleClick += this.ListBox_log_DoubleClick;

			// Enable drag-and-drop for AudioObj(s)
			this.AllowDrop = true;
			this.DragEnter += this.CudaFunctions_DragEnter;
			this.DragDrop += this.CudaFunctions_DragDrop;

			this.ListBox_FillPointers();
			this.UpdatePointerDependentUI();

			this.FormClosing += (s, e) =>
			{
				this._cuda.Dispose();
			};
		}


		// UI
		private void ComboBox_FillDevices(ComboBox comboBox)
		{
			comboBox.Items.Clear();
			var devices = this._cuda.DeviceEntries;
			foreach (var device in devices)
			{
				comboBox.Items.Add(device);
			}

			if (comboBox.Items.Count > 0)
			{
				comboBox.SelectedIndex = 0;
				this.button_initialize.Enabled = true;
			}
		}

		private void ListBox_FillPointers()
		{
			int selectedIndex = this.listBox_pointers.SelectedIndex;
			this.listBox_pointers.SuspendLayout();
			this.listBox_pointers.Items.Clear();
			this.listBox_pointers.Items.AddRange(this._cuda.PointerEntries.ToArray());
			if (selectedIndex >= 0 && selectedIndex < this.listBox_pointers.Items.Count)
			{
				this.listBox_pointers.SelectedIndex = selectedIndex;
			}
			this.listBox_pointers.ResumeLayout();
		}

		private void ComboBox_FillKernels(ComboBox comboBox)
		{
			comboBox.Items.Clear();
			var kernels = this._cuda.GetAvailableKernels();
			foreach (var kernel in kernels)
			{
				comboBox.Items.Add(kernel);
			}
		}

		private IntPtr? GetSelectedIndexPointer()
		{
			if (this.listBox_pointers.SelectedItem is string entryString)
			{
				// Get string between < and >
				string? pointerString = null;
				int startIndex = entryString.IndexOf('<');
				int endIndex = entryString.IndexOf('>');
				if (startIndex >= 0 && endIndex > startIndex)
				{
					pointerString = entryString.Substring(startIndex + 1, endIndex - startIndex - 1);
				}

				// Parse to IntPtr from long.ToString()
				if (pointerString != null && long.TryParse(pointerString, out long pointerLong))
				{
					return new IntPtr(pointerLong);
				}
			}

			return null;
		}

		private void UpdatePointerDependentUI()
		{
			var audio = this.AudioC.Audios.FirstOrDefault(a => a.Pointer == this._selectedIndexPointer);
			bool hasSelection = this._selectedIndexPointer.HasValue;
			this.button_write.Enabled = hasSelection;
			this.button_fft.Enabled = hasSelection;
			this.button_fft.Text = audio?.Form == "f" ? "FFT" : audio?.Form == "c" ? "IFFT" : "Fourier";
		}


		// Async Tasks
		private async Task PushAudioToDeviceAsync(AudioObj audio, bool copyOnly = true)
		{
			if (!this._cuda.Initialized)
			{
				LogCollection.Log("CUDA device is not initialized");
				return;
			}

			var result = await this._cuda.MoveAudioAsync(audio, this._chunkSize, this._overlap, copyOnly);
			if (result.Pointer == IntPtr.Zero)
			{
				LogCollection.Log("Failed to push AudioObj to CUDA device.");
			}

			this.ListBox_FillPointers();
		}

		private async Task PushAllAudiosToDeviceAsync(bool copyOnly = true)
		{
			if (!this._cuda.Initialized)
			{
				MessageBox.Show("CUDA device is not initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// Always use copy semantics; do not alter originals
			copyOnly = true;

			var tasks = this.AudioC.Audios.Select(a => this.PushAudioToDeviceAsync(a, copyOnly));

			await Task.WhenAll(tasks);

			this.ListBox_FillPointers();
		}

		private async Task PullAudioFromDeviceAsync(AudioObj? audio)
		{
			if (!this._cuda.Initialized)
			{
				LogCollection.Log("CUDA device is not initialized");
				return;
			}
			if (audio == null)
			{
				LogCollection.Log("AudioObj is null.");
				return;
			}
			if (audio.Pointer == IntPtr.Zero)
			{
				LogCollection.Log("AudioObj is not on CUDA device.");
				return;
			}

			var result = await this._cuda.MoveAudioAsync(audio,this._chunkSize, this._overlap, false);
			if (result.Data == null || result.Data.Length == 0)
			{
				LogCollection.Log("Failed to pull AudioObj from CUDA device.");
			}

			this.AudioC.Audios.Remove(audio);

			this.ListBox_FillPointers();
			this.UpdatePointerDependentUI();
            WindowMain.Instance?.RefreshAllCollectionViews();
		}

		private async Task PullAllAudiosFromDeviceAsync()
		{
			if (!this._cuda.Initialized)
			{
				MessageBox.Show("CUDA device is not initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var tasks = this.AudioC.Audios.Select(a => this.PullAudioFromDeviceAsync(a));
			await Task.WhenAll(tasks);
			
			this.ListBox_FillPointers();
            WindowMain.Instance?.RefreshAllCollectionViews();
		}

		private async Task BuildArgumentsPanelAsync(Panel? panel = null, float inputWidthPart = 0.7f)
		{
			panel ??= this.panel_arguments;

			var argTypes = this.ArgumentTypes;
			if (argTypes == null || argTypes.Count == 0)
			{
				panel.SuspendLayout();
				try
				{
					panel.Controls.Clear();
				}
				finally
				{
					panel.ResumeLayout();
				}
				return;
			}

			// UI Maße (UI-Thread)
			int panelWidth = panel.ClientSize.Width;
			int controlHeight = 23;
			int controlMargin = 5;
			int x = 5;
			int baseY = 5;

			// Parallel: Deskriptoren vorbereiten (Name/Typ/NumUpDown-Parameter + Y-Position)
			var descriptors = await Task.Run(() =>
			{
				var list = new List<(int y, string name, Type type, bool isNumeric, int decimalPlaces, decimal increment, decimal min, decimal max)>(argTypes.Count);
				int i = 0;
				foreach (var kvp in argTypes)
				{
					string argName = kvp.Key;
					Type argType = kvp.Value;

					bool isNumeric =
						argType == typeof(int) ||
						argType == typeof(float) ||
						argType == typeof(double) ||
						argType == typeof(long) ||
						argType == typeof(short) ||
						argType == typeof(byte);

					int decimalPlaces =
						argType == typeof(int) || argType == typeof(long) || argType == typeof(short) || argType == typeof(byte) ? 0 :
						argType == typeof(float) ? 6 : 14;

					decimal increment =
						argType == typeof(int) || argType == typeof(long) || argType == typeof(short) || argType == typeof(byte) ? 1M :
						argType == typeof(float) ? 0.1M : 0.000001M;

					// sinnvolle Grenzen für NumericUpDown
					decimal min =
						argType == typeof(byte) ? byte.MinValue :
						argType == typeof(short) ? short.MinValue :
						argType == typeof(int) ? int.MinValue :
						argType == typeof(long) ? long.MinValue :
						argType == typeof(float) ? (decimal) -3.4028235E+28 :
						argType == typeof(double) ? (decimal) -1.7976931348623157E+28 :
						decimal.MinValue;

					decimal max =
						argType == typeof(byte) ? byte.MaxValue :
						argType == typeof(short) ? short.MaxValue :
						argType == typeof(int) ? int.MaxValue :
						argType == typeof(long) ? long.MaxValue :
						argType == typeof(float) ? (decimal) 3.4028235E+28 :
						argType == typeof(double) ? (decimal) 1.7976931348623157E+28 :
						decimal.MaxValue;

					int y = baseY + i * (controlHeight + controlMargin);
					list.Add((y, argName, argType, isNumeric, decimalPlaces, increment, min, max));
					i++;
				}
				return list;
			});

			// UI-Thread: Layout vorbereiten, Scrollbars berücksichtigen
			panel.SuspendLayout();
			try
			{
				panel.Controls.Clear();

				// Breiten zunächst ohne Scrollbar berechnen
				int controlWidth = (int) ((panelWidth - controlMargin) * inputWidthPart);
				int labelWidth = panelWidth - controlWidth;

				// Controls erzeugen
				foreach (var d in descriptors)
				{
					var label = new Label
					{
						Text = d.name,
						Location = new Point(x, d.y),
						Size = new Size(labelWidth - controlMargin, controlHeight),
						TextAlign = ContentAlignment.MiddleRight
					};
					panel.Controls.Add(label);

					if (d.isNumeric)
					{
						var numeric = new NumericUpDown
						{
							Name = $"numericUpDown_{d.name}",
							DecimalPlaces = d.decimalPlaces,
							Increment = d.increment,
							Minimum = d.min,
							Maximum = d.max,
							Value = 0,
							Location = new Point(x + labelWidth, d.y),
							Size = new Size(controlWidth - controlMargin, controlHeight)
						};

						if (d.type == typeof(int) || d.type == typeof(long) || d.type == typeof(short) || d.type == typeof(byte))
						{
							this.NumericUpDown_RegisterToAlwaysGoBy2(numeric);
						}

						panel.Controls.Add(numeric);
					}
					else
					{
						var unsupported = new Label
						{
							Text = $"Unsupported type: {d.type.Name}",
							ForeColor = Color.Red,
							Location = new Point(x + labelWidth, d.y),
							Size = new Size(controlWidth - controlMargin, controlHeight),
							TextAlign = ContentAlignment.MiddleLeft
						};
						panel.Controls.Add(unsupported);
					}
				}

				// Nach dem Aufbau prüfen, ob vertikale Scrollbar benötigt ist,
				// und die Eingabefeld-Breite anpassen, falls die vertikale Scrollbar Platz wegnimmt.
				int contentBottom = descriptors.Count == 0 ? baseY : descriptors.Last().y + controlHeight + controlMargin;
				bool needsVScroll = contentBottom > panel.ClientSize.Height;

				if (needsVScroll)
				{
					int vScrollWidth = SystemInformation.VerticalScrollBarWidth;
					int adjustedControlWidth = Math.Max(10, controlWidth - vScrollWidth);
					int adjustedLabelWidth = panelWidth - adjustedControlWidth;

					foreach (Control ctrl in panel.Controls)
					{
						if (ctrl is Label lbl && !lbl.Name.StartsWith("Unsupported type"))
						{
							lbl.Size = new Size(adjustedLabelWidth - controlMargin, controlHeight);
						}
						else if (ctrl is NumericUpDown nud)
						{
							// Position an neues LabelWidth anpassen
							nud.Location = new Point(x + adjustedLabelWidth, nud.Location.Y);
							nud.Size = new Size(adjustedControlWidth - controlMargin, controlHeight);
						}
						else if (ctrl is Label unsupported) // rechter Text
						{
							unsupported.Location = new Point(x + adjustedLabelWidth, unsupported.Location.Y);
							unsupported.Size = new Size(adjustedControlWidth - controlMargin, controlHeight);
						}
					}
				}

				// Horizontal Scroll ermöglichen, falls Breite knapp ist (keine Größenänderung nötig,
				// HScroll wird bei Panel.AutoScroll/HorizontalScroll gesteuert).
				panel.AutoScroll = true;
				panel.HorizontalScroll.Enabled = true;
				panel.HorizontalScroll.Visible = true;
			}
			finally
			{
				panel.ResumeLayout();
			}
		}





		// Drag 'n' Drop AudioObj(s) into the AudioCollection
		private void CudaFunctions_DragEnter(object? sender, DragEventArgs e)
		{
			if (e.Data == null)
			{
				e.Effect = DragDropEffects.None;
				return;
			}

			if (e.Data.GetDataPresent(typeof(AudioObj)) ||
				e.Data.GetDataPresent(typeof(List<AudioObj>)) ||
				e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)) ||
				e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)) ||
				e.Data.GetDataPresent(DataFormats.Serializable))
			{
				e.Effect = DragDropEffects.Copy;
			}
			else
			{
				e.Effect = DragDropEffects.None;
			}
		}

		private async void CudaFunctions_DragDrop(object? sender, DragEventArgs e)
		{
			if (e.Data == null)
			{
				return;
			}

			void AddAudio(AudioObj a)
			{
				if (a != null && !this.AudioC.Audios.Contains(a))
				{
					this.AudioC.Audios.Add(a);
				}
			}

			// Einzelnes AudioObj
			if (e.Data.GetDataPresent(typeof(AudioObj)))
			{
				if (e.Data.GetData(typeof(AudioObj)) is AudioObj audio)
				{
					AddAudio(audio);
				}
			}

			// Liste von AudioObj
			else if (e.Data.GetDataPresent(typeof(List<AudioObj>)))
			{
				if (e.Data.GetData(typeof(List<AudioObj>)) is List<AudioObj> list)
				{
					foreach (var a in list)
					{
						AddAudio(a);
					}
				}
			}

			// IEnumerable<AudioObj>
			else if (e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
			{
				if (e.Data.GetData(typeof(IEnumerable<AudioObj>)) is IEnumerable<AudioObj> enumerable)
				{
					foreach (var a in enumerable)
					{
						AddAudio(a);
					}
				}
			}

			// Drag aus ListBox.SelectedObjectCollection
			else if (e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
			{
				if (e.Data.GetData(typeof(ListBox.SelectedObjectCollection)) is ListBox.SelectedObjectCollection selected)
				{
					foreach (var item in selected)
					{
						if (item is AudioObj a)
						{
							AddAudio(a);
						}
					}
				}
			}

			// Serializable fallback
			else if (e.Data.GetDataPresent(DataFormats.Serializable))
			{
				var data = e.Data.GetData(DataFormats.Serializable);
				if (data is AudioObj a)
				{
					AddAudio(a);
					return;
				}
				if (data is IEnumerable<AudioObj> list2)
				{
					foreach (var x in list2)
					{
						AddAudio(x);
					}
				}
			}

			if (this._cuda.Initialized)
			{
				await this.PushAllAudiosToDeviceAsync(copyOnly: true);
			}
		}

		private void ListBox_log_DoubleClick(object? sender, EventArgs e)
		{
			if (this.listBox_log.SelectedItem is string logEntry)
			{
				Clipboard.SetText(logEntry);
			}
		}

		private void NumericUpDown_RegisterToAlwaysGoBy2(NumericUpDown numericUpDown)
		{
			numericUpDown.Tag = (decimal) numericUpDown.Value;

			numericUpDown.ValueChanged += (s, e) =>
			{
				int value = (int) numericUpDown.Value;
				int oldValue = numericUpDown.Tag is int tagValue ? tagValue : 1024;
				if (value > oldValue)
				{
					value = (int) Math.Clamp(numericUpDown.Minimum, oldValue * 2, numericUpDown.Maximum);
					numericUpDown.Value = value;
				}
				else if (value < oldValue)
				{
					value = (int) Math.Clamp(numericUpDown.Minimum, oldValue / 2, numericUpDown.Maximum);
					numericUpDown.Value = value;
				}
				numericUpDown.Tag = value;
			};
		}




		private void comboBox_devices_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.comboBox_devices.SelectedIndex < 0)
			{
				this.button_initialize.Enabled = false;
			}
		}

		private void listBox_pointers_SelectedIndexChanged(object sender, EventArgs e)
		{
			this.UpdatePointerDependentUI();
		}

		private void numericUpDown_chunkSize_ValueChanged(object sender, EventArgs e)
		{
			int value = (int) this.numericUpDown_chunkSize.Value;
			int oldValue = this.numericUpDown_chunkSize.Tag is int tagValue ? tagValue : 1024;

			if (value > oldValue)
			{
				value = (int) Math.Clamp(this.numericUpDown_chunkSize.Minimum, oldValue * 2, this.numericUpDown_chunkSize.Maximum);
				this.numericUpDown_chunkSize.Value = value;
			}
			else if (value < oldValue)
			{
				value = (int) Math.Clamp(this.numericUpDown_chunkSize.Minimum, oldValue / 2, this.numericUpDown_chunkSize.Maximum);
				this.numericUpDown_chunkSize.Value = value;
			}

			this.numericUpDown_chunkSize.Tag = value;
		}

		private async void button_initialize_Click(object sender, EventArgs e)
		{
			if (this._cuda.Initialized)
			{
				this._cuda.Dispose();
				this.button_initialize.Text = "Initialize";
				this.comboBox_devices.Enabled = true;
				return;
			}

			if (this.comboBox_devices.SelectedIndex < 0)
			{
				MessageBox.Show("Please select a device first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			int index = this.comboBox_devices.SelectedIndex;
			this._cuda.Initialize(index);
			if (!this._cuda.Initialized)
			{
				MessageBox.Show("Failed to initialize CUDA device.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			this.button_initialize.Text = "Dispose";
			this.comboBox_devices.Enabled = false;

			this.ComboBox_FillKernels(this.comboBox_kernels);
			this.ListBox_FillPointers();
			await this.PushAllAudiosToDeviceAsync(copyOnly: true);
		}

		private async void button_write_Click(object sender, EventArgs e)
		{
			if (ModifierKeys.HasFlag(Keys.Control))
			{
				// Pull from device
				await this.PullAllAudiosFromDeviceAsync();
			}
			else
			{
				// Push to device
				if (this._selectedIndexPointer.HasValue)
				{
					await this.PullAudioFromDeviceAsync(this.AudioC.Audios.FirstOrDefault(a => a.Pointer == this._selectedIndexPointer));
				}
			}
		}

		private async void button_fft_Click(object sender, EventArgs e)
		{
			if (!this._cuda.Initialized)
			{
				MessageBox.Show("CUDA device is not initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			if (!this._selectedIndexPointer.HasValue)
			{
				MessageBox.Show("Please select a pointer first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var audio = this.AudioC.Audios.FirstOrDefault(a => a.Pointer == this._selectedIndexPointer);
			if (audio == null)
			{
				MessageBox.Show("Selected pointer does not correspond to any AudioObj.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			await this._cuda.FourierTransformAsync(audio, this._chunkSize, this._overlap);

			this.ListBox_FillPointers();
			this.UpdatePointerDependentUI();
		}

		private async void comboBox_kernels_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (!this._cuda.Initialized || this._selectedKernelName == null)
			{
				return;
			}

			this._cuda.LoadKernel(this._selectedKernelName);
			await this.BuildArgumentsPanelAsync();
		}

		private void button_execute_Click(object sender, EventArgs e)
		{

		}
	}
}
