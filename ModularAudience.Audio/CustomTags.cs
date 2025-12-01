using System;
using System.Collections.Generic;

namespace ModularAudience.Audio
{
	public class CustomTags
	{
		public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

		public string? this[string id]
		{
			get => this.Values.TryGetValue(id, out var v) ? v : null;
			set
			{
				if (string.IsNullOrWhiteSpace(id))
				{
					return;
				}

				if (value is null)
				{
					this.Values.Remove(id);
				}
				else
				{
					this.Values[id] = value;
				}
			}
		}

		public bool HasData => this.Values.Count > 0;
	}
}
