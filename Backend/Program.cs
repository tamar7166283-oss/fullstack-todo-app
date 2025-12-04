using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TodoApi; // וודא שה-Namespace תואם לפרויקט שלך

var builder = WebApplication.CreateBuilder(args);

// ********** 🛠️ התיקון הקריטי לפריסה ב-Render 🛠️ **********
// מכריח את Kestrel להאזין לכתובת 0.0.0.0 ופורט 80, כדי ש-Render יוכל לזהות את הפורט.
builder.WebHost.UseUrls("http://0.0.0.0:80");
// ************************************************************

// 1. הגדרת CORS (החדש - עם דומיין ספציפי)
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCORS", policy =>
    {
        // *** 🎯 כתובת הקליינט הספציפית שלך! ***
        policy.WithOrigins("https://to-do-list-frontend-t80a.onrender.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // חיוני למעבר טוקני JWT
    });
});
// 2. חיבור ל-DB
builder.Services.AddDbContext<PractycodedbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("practycodedb"),
    ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("practycodedb"))));

// 3. הגדרת Swagger (עם תמיכה ב-JWT ב-UI - אופציונלי אך מומלץ)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- 4. הגדרת JWT Authentication ---

// קריאת המפתח מהתצורה. ב-Render, זה יגיע ממשתנה הסביבה שהגדרת (למשל: JWT_SECURITY_KEY)
var securityKey = builder.Configuration["JWT_SECURITY_KEY"];

// בדיקה חיונית: ודא שהמפתח נמצא ואינו קצר מדי
if (string.IsNullOrEmpty(securityKey) || securityKey.Length < 16) 
{
    // במקרה ש-Render לא טען את המפתח, ניתן להשתמש בגיבוי מקומי או לזרוק שגיאה
    securityKey = "FALLBACK_KEY_AT_LEAST_32_CHARS_LONG_FOR_TESTING"; 
}

var keyBytes = Encoding.ASCII.GetBytes(securityKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});
builder.Services.AddAuthorization();
// -------------------------------------

var app = builder.Build();

app.UseCors("FrontendCORS");
app.UseSwagger();
app.UseSwaggerUI(c => { c.RoutePrefix = "swagger"; c.DocumentTitle = "ToDo API Docs"; });

// חובה להפעיל את המידלוור בסדר הזה:
app.UseAuthentication(); 
app.UseAuthorization();

// --- Endpoints להזדהות ---

// הרשמה
app.MapPost("/register", async (PractycodedbContext db, User newUser) =>
{
    if (string.IsNullOrEmpty(newUser.Username) || string.IsNullOrEmpty(newUser.Password))
        return Results.BadRequest("Username and password are required");

    // בדיקה אם המשתמש קיים
    var exists = await db.Users.AnyAsync(u => u.Username == newUser.Username);
    if (exists) return Results.BadRequest("User already exists");

    db.Users.Add(newUser);
    await db.SaveChangesAsync();
    return Results.Ok("User registered successfully");
});

// התחברות
app.MapPost("/login", async (PractycodedbContext db, User loginUser) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == loginUser.Username && u.Password == loginUser.Password);
    
    if (user == null)
        return Results.Unauthorized();

    // יצירת הטוקן
    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user.Id.ToString()),
            new Claim("User", user.Username)
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return Results.Ok(new { token = tokenHandler.WriteToken(token) });
});


// --- Endpoints של משימות (מוגנים ע"י RequireAuthorization) ---

app.MapGet("/items", async (PractycodedbContext db) =>
    await db.Tasks.ToListAsync()).RequireAuthorization();

app.MapPost("/items", async (PractycodedbContext db, TaskItem newItem) =>
{
    db.Tasks.Add(newItem);
    await db.SaveChangesAsync();
    return Results.Created($"/items/{newItem.Id}", newItem);
}).RequireAuthorization();

app.MapPut("/items/{id}", async (PractycodedbContext db, int id, TaskItem updatedItem) =>
{
    var item = await db.Tasks.FindAsync(id);
    if (item is null) return Results.NotFound();

    item.Name = updatedItem.Name;
    item.IsComplete = updatedItem.IsComplete;

    await db.SaveChangesAsync();
    return Results.Ok(item);
}).RequireAuthorization();

app.MapDelete("/items/{id}", async (PractycodedbContext db, int id) =>
{
    var item = await db.Tasks.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Tasks.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// ********** 🛠️ התיקון הקריטי לפריסה ב-Render 🛠️ **********
// מחליף את app.Run("http://localhost:5282") ב-app.Run()
// כדי לאפשר ל-Kestrel להשתמש בכתובת 0.0.0.0:80 שהוגדרה למעלה.
app.Run(); 
// ************************************************************