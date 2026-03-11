using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPasswordVault.API.Services.Interfaces;

namespace MyPasswordVault.API.Services;

public class EmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(HttpClient http, IConfiguration config, ILogger<EmailService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task SendResetPasswordEmailAsync(string toEmail, string resetLink)
    {
        var fromEmail = _config["MailerSend:FromEmail"]!;
        var token = _config["MailerSend:ApiKey"]!;

        var payload = new
        {
            from = new { email = fromEmail },
            to = new[] { new { email = toEmail } },
            subject = "Reset your MyPasswordVault password",
            html = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
                  <title>Reset your password</title>
                  <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css" integrity="sha512-2SwdPD6INVrV/lHTZbO2nodKhrnDdJK9/kg2XD1r9uGqPo1cUbujc+IYdlYdEErWNu69gVcYgdxlmVmzTWnetw==" crossorigin="anonymous" referrerpolicy="no-referrer" />
                </head>
                <body style="margin:0;padding:0;background-color:#03080f;font-family:'Segoe UI',Arial,sans-serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#03080f;padding:40px 16px;">
                    <tr>
                      <td align="center">
                        <table width="100%" style="max-width:520px;background-color:#0c1a2e;border-radius:16px;border:1px solid #1a3558;overflow:hidden;">

                          <!-- Header -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#071526,#0d2a4a);padding:36px 40px 28px;text-align:center;border-bottom:1px solid #1a3558;">
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto;">
                                <tr>
                                  <td style="padding-right:10px;vertical-align:middle;">
                                    <i class="fas fa-lock" style="font-size:20px;color:#00c8ff;"></i>
                                  </td>
                                  <td style="vertical-align:middle;">
                                    <span style="font-size:20px;font-weight:700;color:#d6e4f7;letter-spacing:-0.3px;">MyPasswordVault</span>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- Body -->
                          <tr>
                            <td style="padding:40px 40px 32px;">

                              <!-- Icon circle -->
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto 28px;">
                                <tr>
                                  <td style="width:64px;height:64px;background:rgba(255,165,0,0.1);border:1px solid rgba(255,165,0,0.3);border-radius:50%;text-align:center;vertical-align:middle;">
                                    <i class="fas fa-key" style="font-size:26px;color:#ffa500;line-height:64px;"></i>
                                  </td>
                                </tr>
                              </table>

                              <h1 style="color:#d6e4f7;font-size:22px;font-weight:700;text-align:center;margin:0 0 10px;">Reset your password</h1>
                              <p style="color:#5a7fa8;font-size:15px;text-align:center;margin:0 0 16px;line-height:1.6;">
                                We received a request to reset your master password.
                              </p>

                              <!-- Warning box -->
                              <table cellpadding="0" cellspacing="0" width="100%" style="margin-bottom:28px;">
                                <tr>
                                  <td style="background:#1a1200;border:1px solid #5a3a00;border-radius:10px;padding:14px 18px;text-align:center;">
                                    <i class="fas fa-triangle-exclamation" style="color:#ffa500;margin-right:6px;"></i>
                                    <span style="color:#c8a060;font-size:13px;"><strong style="color:#ffd080;">Warning:</strong> Resetting your password will permanently delete all your vault entries, as they are encrypted with your master password.</span>
                                  </td>
                                </tr>
                              </table>

                              <!-- CTA Button -->
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto 32px;">
                                <tr>
                                  <td style="background:linear-gradient(135deg,#c97000,#a05500);border-radius:10px;">
                                    <a href="{resetLink}"
                                       style="display:inline-block;padding:14px 36px;color:#ffffff;font-size:15px;font-weight:600;text-decoration:none;letter-spacing:0.2px;">
                                      <i class="fas fa-lock-open" style="margin-right:8px;"></i>Reset my password
                                    </a>
                                  </td>
                                </tr>
                              </table>

                              <!-- Divider -->
                              <hr style="border:none;border-top:1px solid #1a3558;margin:0 0 24px;" />

                              <!-- Link fallback -->
                              <p style="color:#5a7fa8;font-size:13px;text-align:center;margin:0 0 6px;">
                                Button not working? Copy and paste this link into your browser:
                              </p>
                              <p style="text-align:center;margin:0 0 24px;">
                                <a href="{resetLink}" style="color:#ffa500;font-size:12px;word-break:break-all;">{resetLink}</a>
                              </p>

                              <!-- Expiry notice -->
                              <table cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                  <td style="background:#071526;border:1px solid #1a3558;border-radius:10px;padding:14px 18px;text-align:center;">
                                    <i class="fas fa-clock" style="color:#5a7fa8;margin-right:6px;"></i>
                                    <span style="color:#5a7fa8;font-size:13px;">This link expires in <strong style="color:#d6e4f7;">15 minutes</strong>.
                                    If you didn't request a reset, you can safely ignore this email.</span>
                                  </td>
                                </tr>
                              </table>

                            </td>
                          </tr>

                          <!-- Footer -->
                          <tr>
                            <td style="background-color:#070f1c;padding:20px 40px;text-align:center;border-top:1px solid #1a3558;">
                              <p style="color:#3a5a7a;font-size:12px;margin:0;line-height:1.7;">
                                <i class="fas fa-lock" style="margin-right:4px;"></i>© 2026 MyPasswordVault &nbsp;·&nbsp; Your passwords, secured with AES-256 encryption.<br/>
                                This is an automated message — please do not reply.
                              </p>
                            </td>
                          </tr>

                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
            """,
            text = $"Reset your MyPasswordVault password\n\nClick the link below to reset your password (expires in 15 minutes):\n{resetLink}\n\nWARNING: This will permanently delete all your vault entries.\nIf you did not request this, ignore this email."
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mailersend.com/v1/email")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("MailerSend error {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException("Email delivery failed. Please try again later.");
        }
    }

    public async Task SendVerificationEmailAsync(string toEmail, string verificationLink)
    {
        var fromEmail = _config["MailerSend:FromEmail"]!;
        var token = _config["MailerSend:ApiKey"]!;

        var payload = new
        {
            from = new { email = fromEmail },
            to = new[] { new { email = toEmail } },
            subject = "Verify your MyPasswordVault email",
            html = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
                  <title>Verify your email</title>
                  <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css" integrity="sha512-2SwdPD6INVrV/lHTZbO2nodKhrnDdJK9/kg2XD1r9uGqPo1cUbujc+IYdlYdEErWNu69gVcYgdxlmVmzTWnetw==" crossorigin="anonymous" referrerpolicy="no-referrer" />
                </head>
                <body style="margin:0;padding:0;background-color:#03080f;font-family:'Segoe UI',Arial,sans-serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#03080f;padding:40px 16px;">
                    <tr>
                      <td align="center">
                        <table width="100%" style="max-width:520px;background-color:#0c1a2e;border-radius:16px;border:1px solid #1a3558;overflow:hidden;">

                          <!-- Header -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#071526,#0d2a4a);padding:36px 40px 28px;text-align:center;border-bottom:1px solid #1a3558;">
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto;">
                                <tr>
                                  <td style="padding-right:10px;vertical-align:middle;">
                                    <i class="fas fa-lock" style="font-size:20px;color:#00c8ff;"></i>
                                  </td>
                                  <td style="vertical-align:middle;">
                                    <span style="font-size:20px;font-weight:700;color:#d6e4f7;letter-spacing:-0.3px;">MyPasswordVault</span>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- Body -->
                          <tr>
                            <td style="padding:40px 40px 32px;">

                              <!-- Icon circle — table-based centering for email clients -->
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto 28px;">
                                <tr>
                                  <td style="width:64px;height:64px;background:rgba(0,200,255,0.1);border:1px solid rgba(0,200,255,0.25);border-radius:50%;text-align:center;vertical-align:middle;">
                                    <i class="fas fa-envelope" style="font-size:26px;color:#00c8ff;line-height:64px;"></i>
                                  </td>
                                </tr>
                              </table>

                              <h1 style="color:#d6e4f7;font-size:22px;font-weight:700;text-align:center;margin:0 0 10px;">Confirm your email address</h1>
                              <p style="color:#5a7fa8;font-size:15px;text-align:center;margin:0 0 32px;line-height:1.6;">
                                Thanks for signing up! Click the button below to verify your email and activate your vault.
                              </p>

                              <!-- CTA Button -->
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto 32px;">
                                <tr>
                                  <td style="background:linear-gradient(135deg,#1d6ef7,#0d4db5);border-radius:10px;">
                                    <a href="{verificationLink}"
                                       style="display:inline-block;padding:14px 36px;color:#ffffff;font-size:15px;font-weight:600;text-decoration:none;letter-spacing:0.2px;">
                                      <i class="fas fa-shield-halved" style="margin-right:8px;"></i>Verify Email Address
                                    </a>
                                  </td>
                                </tr>
                              </table>

                              <!-- Divider -->
                              <hr style="border:none;border-top:1px solid #1a3558;margin:0 0 24px;" />

                              <!-- Link fallback -->
                              <p style="color:#5a7fa8;font-size:13px;text-align:center;margin:0 0 6px;">
                                Button not working? Copy and paste this link into your browser:
                              </p>
                              <p style="text-align:center;margin:0 0 24px;">
                                <a href="{verificationLink}" style="color:#00c8ff;font-size:12px;word-break:break-all;">{verificationLink}</a>
                              </p>

                              <!-- Expiry notice -->
                              <table cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                  <td style="background:#071526;border:1px solid #1a3558;border-radius:10px;padding:14px 18px;text-align:center;">
                                    <i class="fas fa-clock" style="color:#5a7fa8;margin-right:6px;"></i>
                                    <span style="color:#5a7fa8;font-size:13px;">This link expires in <strong style="color:#d6e4f7;">15 minutes</strong>.
                                    If you didn't create an account, you can safely ignore this email.</span>
                                  </td>
                                </tr>
                              </table>

                            </td>
                          </tr>

                          <!-- Footer -->
                          <tr>
                            <td style="background-color:#070f1c;padding:20px 40px;text-align:center;border-top:1px solid #1a3558;">
                              <p style="color:#3a5a7a;font-size:12px;margin:0;line-height:1.7;">
                                <i class="fas fa-lock" style="margin-right:4px;"></i>© 2026 MyPasswordVault &nbsp;·&nbsp; Your passwords, secured with AES-256 encryption.<br/>
                                This is an automated message — please do not reply.
                              </p>
                            </td>
                          </tr>

                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
            """,
            text = $"Verify your MyPasswordVault email\n\nClick the link below to activate your account:\n{verificationLink}\n\nThis link expires in 15 minutes.\nIf you didn't create an account, ignore this email."
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mailersend.com/v1/email")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("MailerSend error {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException("Email delivery failed. Please try again later.");
        }
    }

    public async Task SendNewLoginAlertAsync(string toEmail, string ipAddress, string userAgent, DateTime loginTime)
    {
        var fromEmail = _config["MailerSend:FromEmail"]!;
        var apiToken = _config["MailerSend:ApiKey"]!;
        var formattedTime = loginTime.ToString("dddd, MMMM d, yyyy 'at' HH:mm 'UTC'");

        var payload = new
        {
            from = new { email = fromEmail },
            to = new[] { new { email = toEmail } },
            subject = "New sign-in to your MyPasswordVault account",
            html = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
                  <title>New sign-in detected</title>
                  <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css" integrity="sha512-2SwdPD6INVrV/lHTZbO2nodKhrnDdJK9/kg2XD1r9uGqPo1cUbujc+IYdlYdEErWNu69gVcYgdxlmVmzTWnetw==" crossorigin="anonymous" referrerpolicy="no-referrer" />
                </head>
                <body style="margin:0;padding:0;background-color:#03080f;font-family:'Segoe UI',Arial,sans-serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#03080f;padding:40px 16px;">
                    <tr>
                      <td align="center">
                        <table width="100%" style="max-width:520px;background-color:#0c1a2e;border-radius:16px;border:1px solid #1a3558;overflow:hidden;">

                          <!-- Header -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#071526,#0d2a4a);padding:36px 40px 28px;text-align:center;border-bottom:1px solid #1a3558;">
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto;">
                                <tr>
                                  <td style="padding-right:10px;vertical-align:middle;">
                                    <i class="fas fa-lock" style="font-size:20px;color:#00c8ff;"></i>
                                  </td>
                                  <td style="vertical-align:middle;">
                                    <span style="font-size:20px;font-weight:700;color:#d6e4f7;letter-spacing:-0.3px;">MyPasswordVault</span>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- Body -->
                          <tr>
                            <td style="padding:40px 40px 32px;">

                              <!-- Icon circle -->
                              <table cellpadding="0" cellspacing="0" style="margin:0 auto 28px;">
                                <tr>
                                  <td style="width:64px;height:64px;background:rgba(255,60,60,0.1);border:1px solid rgba(255,60,60,0.3);border-radius:50%;text-align:center;vertical-align:middle;">
                                    <i class="fas fa-shield-exclamation" style="font-size:26px;color:#ff4444;line-height:64px;"></i>
                                  </td>
                                </tr>
                              </table>

                              <h1 style="color:#d6e4f7;font-size:22px;font-weight:700;text-align:center;margin:0 0 10px;">New sign-in detected</h1>
                              <p style="color:#5a7fa8;font-size:15px;text-align:center;margin:0 0 28px;line-height:1.6;">
                                Your MyPasswordVault account was just accessed from a new device or location.
                              </p>

                              <!-- Login details card -->
                              <table cellpadding="0" cellspacing="0" width="100%" style="margin-bottom:28px;">
                                <tr>
                                  <td style="background:#071526;border:1px solid #1a3558;border-radius:12px;padding:20px 24px;">

                                    <table cellpadding="0" cellspacing="0" width="100%">
                                      <tr>
                                        <td style="padding-bottom:14px;border-bottom:1px solid #1a3558;">
                                          <p style="margin:0 0 4px;color:#3a5a7a;font-size:11px;text-transform:uppercase;letter-spacing:0.8px;">Time</p>
                                          <p style="margin:0;color:#d6e4f7;font-size:14px;">
                                            <i class="fas fa-clock" style="color:#5a7fa8;margin-right:6px;"></i>{formattedTime}
                                          </p>
                                        </td>
                                      </tr>
                                      <tr>
                                        <td style="padding-top:14px;padding-bottom:14px;border-bottom:1px solid #1a3558;">
                                          <p style="margin:0 0 4px;color:#3a5a7a;font-size:11px;text-transform:uppercase;letter-spacing:0.8px;">IP Address</p>
                                          <p style="margin:0;color:#d6e4f7;font-size:14px;">
                                            <i class="fas fa-location-dot" style="color:#5a7fa8;margin-right:6px;"></i>{ipAddress}
                                          </p>
                                        </td>
                                      </tr>
                                      <tr>
                                        <td style="padding-top:14px;">
                                          <p style="margin:0 0 4px;color:#3a5a7a;font-size:11px;text-transform:uppercase;letter-spacing:0.8px;">Browser / Device</p>
                                          <p style="margin:0;color:#d6e4f7;font-size:14px;word-break:break-word;">
                                            <i class="fas fa-display" style="color:#5a7fa8;margin-right:6px;"></i>{System.Net.WebUtility.HtmlEncode(userAgent)}
                                          </p>
                                        </td>
                                      </tr>
                                    </table>

                                  </td>
                                </tr>
                              </table>

                              <!-- If it was you -->
                              <table cellpadding="0" cellspacing="0" width="100%" style="margin-bottom:16px;">
                                <tr>
                                  <td style="background:#071a10;border:1px solid #1a4530;border-radius:10px;padding:14px 18px;text-align:center;">
                                    <i class="fas fa-circle-check" style="color:#4caf50;margin-right:6px;"></i>
                                    <span style="color:#7ac99a;font-size:13px;">If this was you, no action is needed — you're all set.</span>
                                  </td>
                                </tr>
                              </table>

                              <!-- If it wasn't you -->
                              <table cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                  <td style="background:#1a0f0f;border:1px solid #5a2020;border-radius:10px;padding:14px 18px;text-align:center;">
                                    <i class="fas fa-triangle-exclamation" style="color:#ff4444;margin-right:6px;"></i>
                                    <span style="color:#c87070;font-size:13px;"><strong style="color:#ff8080;">Not you?</strong> Change your master password immediately and enable two-factor authentication.</span>
                                  </td>
                                </tr>
                              </table>

                            </td>
                          </tr>

                          <!-- Footer -->
                          <tr>
                            <td style="background-color:#070f1c;padding:20px 40px;text-align:center;border-top:1px solid #1a3558;">
                              <p style="color:#3a5a7a;font-size:12px;margin:0;line-height:1.7;">
                                <i class="fas fa-lock" style="margin-right:4px;"></i>© 2026 MyPasswordVault &nbsp;·&nbsp; Your passwords, secured with AES-256 encryption.<br/>
                                This is an automated security alert — please do not reply.
                              </p>
                            </td>
                          </tr>

                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
            """,
            text = $"New sign-in to your MyPasswordVault account\n\nTime: {formattedTime}\nIP Address: {ipAddress}\nBrowser/Device: {userAgent}\n\nIf this was you, no action is needed.\nIf this was NOT you, change your master password immediately and enable two-factor authentication."
        };

        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mailersend.com/v1/email")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await _http.SendAsync(req);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("MailerSend error {StatusCode}: {Body}", (int)response.StatusCode, body);
            // Non-fatal: log but do not block the login
        }
    }
}