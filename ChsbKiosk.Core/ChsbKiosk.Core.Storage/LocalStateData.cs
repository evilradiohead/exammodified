using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Storage;

public sealed class LocalStateData
{
	public string? AuthToken { get; set; }

	public string? StudentName { get; set; }

	public DateTimeOffset? TokenSavedAt { get; set; }

	public bool CameraSeen { get; set; }

	public Dictionary<string, AttemptLocalState> Attempts { get; set; } = new Dictionary<string, AttemptLocalState>(StringComparer.Ordinal);
}
