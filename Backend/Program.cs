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
// ========================================
// 🔌 CONNECTION STRING - CONFIGURATION HIERARCHY
// ========================================
// .NET Core reads in this order (highest priority wins):
// 1. Environment VariablesxxxxxxxctionStrings__practycodedb (CLEVER CLOUD!)
// 2. appsettings.{Environment}.json (Development/Production)
// 3. appsettings.json (base config)
//
// ⚠️ For Clever Cloud Production:
// Set Environment Variable: ConnectionStrings__practycodedb
// The __ (double underscore) tells .NET to map to ConnectionStrings:practycodedb

string? connectionString = builder.Configuration.GetConnectionString("practycodedb");

if (string.IsNullOrEmpty(connectionString))
{
    throw new Exception(
        "❌ FATAL: No ConnectionString found for 'practycodedb'!\n\n" +
        "Configure one of:\n" +
        "  1. appsettings.json: { \"ConnectionStrings\": { \"practycodedb\": \"...\" } }\n" +
        "  2. appsettings.Development.json (for local development)\n" +
        "  3. Environment Variable: ConnectionStrings__practycodedb (for Clever Cloud)\n\n" +
        "For Clever Cloud: Use pattern ConnectionStrings__practycodedb with MySQL URL"
    );
}

// Log which configuration source is being used
bool isLocal = connectionString.Contains("localhost") || connectionString.Contains("127.0.0.1");

Console.WriteLine($"🚀 Application starting...");
Console.WriteLine($"📍 Database: {(isLocal ? "LOCAL" : "CLEVER CLOUD")}");

// 2. חיבור ל-DB
builder.Services.AddDbContext<PractycodedbContext>(options =>
{
    options.UseMySql(connectionString,
        ServerVersion.AutoDetect(connectionString));
    
    // DEBUG: Log SQL queries
    if (!builder.Environment.IsProduction())
    {
        options.LogTo(Console.WriteLine, LogLevel.Information);
    }
});

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

// קריאת המפתח מהתצורה. בProduction (Render), זה יגיע ממשתנה הסביבה JWT_SECURITY_KEY
var securityKey = builder.Configuration["JWT_SECURITY_KEY"];

// בדיקה: ודא שהמפתח תקין
if (string.IsNullOrEmpty(securityKey)) 
{
    throw new Exception(
        "❌ FATAL: JWT_SECURITY_KEY is not configured!\n" +
        "In Render Console, set Environment Variable:\n" +
        "  Name: JWT_SECURITY_KEY\n" +
        "  Value: your-secret-key-at-least-32-characters-long-for-production!"
    );
}

if (securityKey.Length < 32)
{
    throw new Exception(
        $"❌ FATAL: JWT_SECURITY_KEY is too short!\n" +
        $"  Current length: {securityKey.Length} characters\n" +
        $"  Required: at least 32 characters"
    );
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

var app = builder.Build();

// ✅ Database connection is configured via ConnectionStrings__practycodedb
// Tables already exist in Clever Cloud - no need to create them
Console.WriteLine($"🚀 Application starting in {(builder.Environment.IsDevelopment() ? "Development" : "Production")} mode");
Console.WriteLine($"📍 Connected to database via configuration");

app.UseCors("FrontendCORS");
app.UseSwagger();
app.UseSwaggerUI(c => { c.RoutePrefix = "swagger"; c.DocumentTitle = "ToDo API Docs"; });

// חובה להפעיל את המידלוור בסדר הזה:
app.UseAuthentication(); 
app.UseAuthorization();

// --- Debug Endpoint: Check Database ---
app.MapGet("/check-db", async (PractycodedbContext db) =>
{
    try
    {
        bool canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
            return Results.BadRequest(new { status = "FAILED", error = "Cannot connect" });
        
        var usersCount = await db.Users.CountAsync();
        var tasksCount = await db.Tasks.CountAsync();
        
        return Results.Ok(new { status = "OK", usersCount, tasksCount });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { status = "ERROR", error = ex.Message });
    }
});

// --- Endpoints להזדהות ---

app.MapPost("/register", async (PractycodedbContext db, User newUser) =>
{
    try
    {
        if (string.IsNullOrEmpty(newUser.Name) || string.IsNullOrEmpty(newUser.Password))
            return Results.BadRequest(new { error = "Name and password are required" });

        var exists = await db.Users.AnyAsync(u => u.Name == newUser.Name);
        if (exists)
            return Results.BadRequest(new { error = "User already exists" });

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
                new Claim(ClaimTypes.Name, newUser.Name ?? "")
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
        return Results.BadRequest(new { error = "Registration failed", details = ex.Message });
    }
});

// התחברות
app.MapPost("/login", async (PractycodedbContext db, User loginUser) =>
{
    try
    {
        if (string.IsNullOrEmpty(loginUser.Name) || string.IsNullOrEmpty(loginUser.Password))
            return Results.BadRequest(new { error = "Name and password are required" });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == loginUser.Name && u.Password == loginUser.Password);
        
        if (user == null)
            return Results.BadRequest(new { error = "Invalid username or password" });

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name ?? "")
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
        return Results.BadRequest(new { error = "Login failed", details = ex.Message });
    }
});


// --- Endpoints של משימות (מוגנים ע"י RequireAuthorization) ---

app.MapGet("/items", async (PractycodedbContext db) =>
{
    var items = await db.Tasks.ToListAsync();
    return Results.Ok(items);
}).RequireAuthorization();

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