using System.Net;
using System.Net.Mail;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;

namespace EmployeeApi.Services;

public sealed class PasswordSetupService(IConfiguration config, UserManager<ApplicationUser> users)
{
    public async Task Send(string email)
    {
        if (!config.GetValue("Notifications:EmailEnabled", true)) return;
        var host = config["Email:SmtpHost"];
        var from = config["Email:From"];
        var setupUrl = config["Email:PasswordSetupUrl"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || !Uri.TryCreate(setupUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !uri.IsLoopback))
            throw new ApiException(503, "Password setup email is not configured. Contact the administrator.");
        var user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user is null) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        // Fragment avoids sending the token to the web server in URLs or access logs.
        var link = $"{setupUrl}#userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        using var message = new MailMessage(from, user.Email!, "Set your PeopleOps password", $"Set your password using this link (expires in one hour):\n{link}\nIf you did not request this, ignore this email.");
        using var smtp = new SmtpClient(host, config.GetValue("Email:SmtpPort", 587)) { EnableSsl = config.GetValue("Email:EnableSsl", true) };
        if (!string.IsNullOrEmpty(config["Email:Username"])) smtp.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
        try { await smtp.SendMailAsync(message); }
        catch (SmtpException) { throw new ApiException(503, "Password setup email could not be delivered. Please retry later."); }
    }
}
