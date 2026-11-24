using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Core.Exam;

public interface IAnswerApi
{
	Task<ApiResult<Empty>> SaveAnswerAsync(string testResultId, AnswerInput answer, CancellationToken ct);

	Task<ApiResult<BatchSaveResponse>> SaveAnswersBatchAsync(string testResultId, IReadOnlyList<AnswerInput> answers, CancellationToken ct);
}
