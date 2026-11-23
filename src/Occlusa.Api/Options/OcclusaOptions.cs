namespace Occlusa.Api.Options;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";
    public string ConnectionString { get; set; } = string.Empty;
    public string JwtSecret { get; set; } = string.Empty;
    public string ClinicGroupId { get; set; } = "OCCLUSA-MY";
}

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v21.0";
}

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
}

public sealed class ClinicOptions
{
    public const string SectionName = "Clinic";
    public string DefaultBranchId { get; set; } = "HQ-KL";
    public decimal CommissionRate { get; set; } = 0.40m;
    public string BackupDirectory { get; set; } = "backups";
}
