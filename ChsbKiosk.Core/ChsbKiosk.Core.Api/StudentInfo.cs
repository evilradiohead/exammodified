namespace ChsbKiosk.Core.Api;

public sealed class StudentInfo
{
	public string Id { get; set; } = "";

	public string Username { get; set; } = "";

	public string FirstName { get; set; } = "";

	public string LastName { get; set; } = "";

	public string? MiddleName { get; set; }

	public string? Grade { get; set; }

	public string? School { get; set; }

	public string FullName => (LastName + " " + FirstName).Trim();
}
