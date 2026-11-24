using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Core.Exam;

public sealed class ApiClientAnswerApi(ApiClient client) : IAnswerApi
{
	public Task<ApiResult<Empty>> SaveAnswerAsync(string testResultId, AnswerInput answer, CancellationToken ct)
	{
		return client.SaveAnswerAsync(testResultId, answer, ct);
	}

	public Task<ApiResult<BatchSaveResponse>> SaveAnswersBatchAsync(string testResultId, IReadOnlyList<AnswerInput> answers, CancellationToken ct)
	{
		return client.SaveAnswersBatchAsync(testResultId, answers, ct);
	}
}
