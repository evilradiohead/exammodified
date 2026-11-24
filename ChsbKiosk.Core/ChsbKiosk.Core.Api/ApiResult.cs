namespace ChsbKiosk.Core.Api;

public sealed class ApiResult<T>
{
	public bool Ok { get; init; }

	public int Status { get; init; }

	public T? Value { get; init; }

	public string? Error { get; init; }

	public string? Code { get; init; }

	public bool IsNetworkError { get; init; }

	public bool IsUnauthorized => Status == 401;

	public bool IsTransient
	{
		get
		{
			if (!IsNetworkError && Status != 429)
			{
				return Status >= 500;
			}
			return true;
		}
	}

	public string ErrorOr(string fallback)
	{
		if (!string.IsNullOrWhiteSpace(Error))
		{
			return Error;
		}
		return fallback;
	}

	public static ApiResult<T> Success(T value, int status)
	{
		return new ApiResult<T>
		{
			Ok = true,
			Status = status,
			Value = value
		};
	}

	public static ApiResult<T> Failure(int status, string? error, string? code = null)
	{
		return new ApiResult<T>
		{
			Ok = false,
			Status = status,
			Error = error,
			Code = code
		};
	}

	public static ApiResult<T> Network(string error)
	{
		return new ApiResult<T>
		{
			Ok = false,
			Status = 0,
			Error = error,
			IsNetworkError = true
		};
	}
}
