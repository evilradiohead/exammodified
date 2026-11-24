using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Storage;

public sealed class AttemptLocalState
{
	public List<LocalAnswer> Pending { get; set; } = new List<LocalAnswer>();

	public int WarningsUsed { get; set; }

	public bool CameraWaived { get; set; }

	public string? PendingViolationType { get; set; }

	public int LastQuestionIndex { get; set; }

	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
