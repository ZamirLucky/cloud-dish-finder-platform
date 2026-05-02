using DishFinder.DataAccess;
using DishFinder.Interfaces;
using DishFinder.Services;
using Google.Cloud.Firestore;
using Google.Cloud.SecretManager.V1;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

string projectId = builder.Configuration["GoogleCloud:ProjectId"]
    ?? throw new InvalidOperationException("Missing GoogleCloud:ProjectId");

string clientIdSecretName = builder.Configuration["Secrets:GoogleOAuthClientIdSecretName"]
    ?? throw new InvalidOperationException("Missing Secrets:GoogleOAuthClientIdSecretName");

string clientSecretSecretName = builder.Configuration["Secrets:GoogleOAuthClientSecretSecretName"]
    ?? throw new InvalidOperationException("Missing Secrets:GoogleOAuthClientSecretSecretName");

string googleClientId = SecretHelper.ReadSecret(projectId, clientIdSecretName);
string googleClientSecret = SecretHelper.ReadSecret(projectId, clientSecretSecretName);


builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.Scope.Add("profile");
        options.Events.OnCreatingTicket = context =>
        {
            var email = context.User.GetProperty("email").GetString();
            var picture = context.User.GetProperty("picture").GetString();

            if (!string.IsNullOrWhiteSpace(email))
                context.Identity?.AddClaim(new Claim("email", email));

            if (!string.IsNullOrWhiteSpace(picture))
                context.Identity?.AddClaim(new Claim("picture", picture));

            return Task.CompletedTask;
        };
    });

builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return FirestoreDb.Create(projectId);
});

builder.Services.AddScoped<IBucketStorageService, BucketStorageService>();
builder.Services.AddScoped<IFirestoreMenuRepository, FirestoreMenuRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
