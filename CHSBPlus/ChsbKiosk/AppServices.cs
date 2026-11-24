using System;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Exam;
using ChsbKiosk.Core.Proctor;
using ChsbKiosk.Core.Storage;
using ChsbKiosk.Security;

namespace ChsbKiosk;

public sealed class AppServices : IDisposable
{
	public KioskConfig Config { get; }

	public ApiClient Api { get; }

	public LocalStateStore Store { get; }

	public ExamClock Clock { get; } = new ExamClock();

	public ProctorSettings Proctor { get; set; } = new ProctorSettings().Sanitized();

	public int HeartbeatSec { get; set; } = 120;

	public string? StudentLogin { get; set; }

	private AppServices(KioskConfig config, ApiClient api, LocalStateStore store)
	{
		Config = config;
		Api = api;
		Store = store;
	}

	public static AppServices Create(KioskConfig config)
	{
		DeviceInfo device = DeviceIdentity.Create(config);
		ApiClient api = new ApiClient(config.ApiBaseUri, device, "2.1.1", config.AllowInsecureHttp);
		Logger.Info($"{"Test platformasi"} {"2.1.1"} ishga tushdi. Server: {config.ApiBaseUri.GetLeftPart(UriPartial.Authority)}");
		LocalStateStore localStateStore = new LocalStateStore(config.StateFile, new DpapiProtector());
		localStateStore.Load(DateTimeOffset.UtcNow);
		return new AppServices(config, api, localStateStore);
	}

	public void PersistSession(string? studentName)
	{
		Store.SetAuth(Api.ExportAuthToken(), studentName, DateTimeOffset.UtcNow);
	}

	public void ForgetSession()
	{
		StudentLogin = null;
		Api.ClearSession();
		Store.SetAuth(null, null, DateTimeOffset.UtcNow);
	}

	public void Dispose()
	{
		Api.Dispose();
	}
}
