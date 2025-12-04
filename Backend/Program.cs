using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TodoApi; // וודא שה-Namespace תואם לפרויקט שלך

var builder = WebApplication.CreateBuilder(args);

// ********** 🛠️ התיקון הקריטי לפריסה ב-Render 🛠️ **********
// בפיתוח: השתמש בפורט 5282, ב-Render: השתמש ב-0.0.0.0:80
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5282");
}
else
{
    builder.WebHost.UseUrls("http://0.0.0.0:80");
}
// ************************************************************

// 1. הגדרת CORS (חוקי וידידותיים - מרשה לכולם)
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCORS", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod(); // מרשה לכל המקורות
    });
});
// 2. חיבור ל-DB
builder.Services.AddDbContext<PractycodedbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("practycodedb"),
    ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("practycodedb"))));

// 3. הגדרת Swagger (עם תמיכה ב-JWT ב-UI - אופציונלי אך מומלץ)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\""
    });
    
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

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

// 🔨 CREATE DATABASE AND TABLES AUTOMATICALLY (for both local and Render)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PractycodedbContext>();
    try
    {
        // יצירת הטבלאות אם לא קיימות
        db.Database.EnsureCreated();
        Console.WriteLine("✅ Database and tables are ready!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Database warning: {ex.Message}");
    }
}

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
    try
    {
        if (string.IsNullOrEmpty(newUser.Username) || string.IsNullOrEmpty(newUser.Password))
            return Results.BadRequest("Username and password are required");

        // בדיקה אם המשתמש קיים
        var exists = await db.Users.AnyAsync(u => u.Username == newUser.Username);
        if (exists) return Results.BadRequest("User already exists");

        // הוסף משתמש חדש
        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        // יצירת טוקן אחרי הרשמה מוצלחת - משתמש ב-keyBytes מ-Closure
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
                new Claim(ClaimTypes.Name, newUser.Username ?? "")
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Results.Ok(new { token = tokenString, message = "Registration successful" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Register error: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// התחברות
app.MapPost("/login", async (PractycodedbContext db, User loginUser) =>
{
    try
    {
        if (string.IsNullOrEmpty(loginUser.Username) || string.IsNullOrEmpty(loginUser.Password))
            return Results.BadRequest("Username and password are required");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == loginUser.Username && u.Password == loginUser.Password);
        
        if (user == null)
            return Results.Unauthorized();

        // יצירת הטוקן
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username ?? "")
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);
        
        return Results.Ok(new { token = tokenString, message = "Login successful" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login error: {ex.Message}");
        return Results.StatusCode(500);
    }
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