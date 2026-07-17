using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsmUpdateUtility.Services;
using Hangfire;
using OsmUpdateUtility.HangfireJobs;

namespace OsmUpdateUtility.Pages;

[Authorize]
public class SettingsModel : PageModel
{
    private readonly ISettingsService _settings;
    private readonly IAuthService _authService;
    private readonly IRecurringJobManager _recurringJobManager;

    public SettingsModel(ISettingsService settings, IAuthService authService, IRecurringJobManager recurringJobManager)
    {
        _settings = settings;
        _authService = authService;
        _recurringJobManager = recurringJobManager;
    }

    [BindProperty] public string UpdateMode { get; set; } = "Confirm";
    [BindProperty] public string ScheduleType { get; set; } = "Daily";
    [BindProperty] public int ScheduleHour { get; set; } = 3;
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    [BindProperty] public string NewUsername { get; set; } = "";
    [BindProperty] public string NewUserPassword { get; set; } = "";
    [BindProperty] public string NewUserConfirmPassword { get; set; } = "";

    public string? Message { get; set; }
    public string? PasswordMessage { get; set; }
    public string? CreateUserMessage { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["ActivePage"] = "Settings";
        ViewData["Title"] = "Настройки";

        UpdateMode = await _settings.GetAsync("UpdateMode", "Confirm");
        ScheduleType = await _settings.GetAsync("ScheduleType", "Daily");
        ScheduleHour = await _settings.GetIntAsync("ScheduleHour", 3);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ScheduleHour < 0) ScheduleHour = 0;
        if (ScheduleHour > 23) ScheduleHour = 23;

        await _settings.SetAsync("UpdateMode", UpdateMode);
        await _settings.SetAsync("ScheduleType", ScheduleType);
        await _settings.SetIntAsync("ScheduleHour", ScheduleHour);

        UpdateHangfireSchedule(ScheduleType, ScheduleHour);

        Message = "Настройки успешно сохранены и применены.";

        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        if (NewPassword != ConfirmPassword) { PasswordMessage = "Пароли не совпадают."; return Page(); }
        if (NewPassword.Length < 6) { PasswordMessage = "Минимум 6 символов."; return Page(); }

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) { PasswordMessage = "Пользователь не найден."; return Page(); }

        var success = await _authService.ChangePasswordAsync(username, CurrentPassword, NewPassword);
        if (success)
        {
            PasswordMessage = "Пароль успешно изменен.";
            CurrentPassword = ""; NewPassword = ""; ConfirmPassword = "";
        }
        else { PasswordMessage = "Неверный текущий пароль."; }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewUserPassword))
        {
            CreateUserMessage = "Заполните имя пользователя и пароль.";
            return Page();
        }

        if (NewUserPassword != NewUserConfirmPassword)
        {
            CreateUserMessage = "Пароли не совпадают.";
            return Page();
        }

        if (NewUserPassword.Length < 6)
        {
            CreateUserMessage = "Пароль должен содержать минимум 6 символов.";
            return Page();
        }

        if (await _authService.UsernameExistsAsync(NewUsername))
        {
            CreateUserMessage = $"Пользователь '{NewUsername}' уже существует.";
            return Page();
        }

        await _authService.CreateUserAsync(NewUsername, NewUserPassword);
        CreateUserMessage = $"Пользователь '{NewUsername}' успешно создан!";

        NewUsername = "";
        NewUserPassword = "";
        NewUserConfirmPassword = "";

        return Page();
    }

    private void UpdateHangfireSchedule(string type, int hour)
    {
        string cronExpression = type switch
        {
            "Daily" => $"0 {hour} * * *",
            "Every3Days" => $"0 {hour} */3 * *",
            "Weekly" => $"0 {hour} * * 0",
            _ => $"0 {hour} * * *"
        };

        var localTimeZone = TimeZoneInfo.Local;
        _recurringJobManager.AddOrUpdate<CheckUpdatesJob>(
            "check-osm-updates",
            job => job.ExecuteAsync(),
            cronExpression,
            new RecurringJobOptions { TimeZone = localTimeZone });
    }
}