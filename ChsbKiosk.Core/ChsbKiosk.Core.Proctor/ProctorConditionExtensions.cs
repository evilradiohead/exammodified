namespace ChsbKiosk.Core.Proctor;

public static class ProctorConditionExtensions
{
	public static string ToViolationType(this ProctorCondition condition)
	{
		return condition switch
		{
			ProctorCondition.LookingAway => "LOOKING_AWAY", 
			ProctorCondition.MultipleFaces => "MULTIPLE_FACES", 
			ProctorCondition.NoFace => "NO_FACE", 
			ProctorCondition.CameraCovered => "CAMERA_COVERED", 
			ProctorCondition.CameraOff => "CAMERA_OFF", 
			_ => "", 
		};
	}

	public static string ToMessage(this ProctorCondition condition)
	{
		return condition switch
		{
			ProctorCondition.LookingAway => "Siz yon tomonga qarayapsiz", 
			ProctorCondition.MultipleFaces => "Kadrda boshqa odam aniqlandi", 
			ProctorCondition.NoFace => "Siz kamerada ko'rinmayapsiz", 
			ProctorCondition.CameraCovered => "Kamera to'sib qo'yilgan", 
			ProctorCondition.CameraOff => "Kamera o'chirilgan yoki uzilgan", 
			_ => "", 
		};
	}

	public static string ToInstruction(this ProctorCondition condition)
	{
		return condition switch
		{
			ProctorCondition.LookingAway => "Darhol ekranga qarang", 
			ProctorCondition.MultipleFaces => "Kamera oldida faqat o'zingiz bo'lishingiz kerak", 
			ProctorCondition.NoFace => "Kamera oldiga qayting va ekranga qarang", 
			ProctorCondition.CameraCovered => "Kamerani darhol oching", 
			ProctorCondition.CameraOff => "Kamerani darhol qayta ulang", 
			_ => "", 
		};
	}
}
