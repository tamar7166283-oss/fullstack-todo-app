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
if (builder.Environment.IsDevelopment())
{
    Console.WriteLine("🏠 Development: ConnectionString from appsettings.Development.json");
}
else
{
    Console.WriteLine("☁️ Production: ConnectionString from Environment Variable or appsettings.json");
}

Console.WriteLine($"📍 Target Database: {(isLocal ? "LOCAL DATABASE 🏠" : "CLEVER CLOUD REMOTE ☁️")}");

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

// 🔍 DEBUG - הדפס את גודל המפתח
Console.WriteLine("════════════════════════════════════════");
Console.WriteLine("🔑 JWT_SECURITY_KEY validated:");
Console.WriteLine($"   Length: {securityKey.Length} chars ({keyBytes.Length * 8} bits)");
Console.WriteLine($"   Status: ✅ OK");
Console.WriteLine("════════════════════════════════════════");

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

// --- Endpoints להזדהות ---

// הרשמה
app.MapPost("/register", async (PractycodedbContext db, User newUser) =>
{
    try
    {
        Console.WriteLine($"📝 [REGISTER] Request received - Name: {newUser.Name}");
        
        if (string.IsNullOrEmpty(newUser.Name) || string.IsNullOrEmpty(newUser.Password))
        {
            Console.WriteLine($"   ❌ Validation failed: Missing credentials");
            return Results.BadRequest("Name and password are required");
        }

        Console.WriteLine($"   ✅ Validation passed");
        Console.WriteLine($"   🔍 Checking if user exists...");

        // בדיקה אם המשתמש קיים
        Console.WriteLine($"   🔍 SQL: SELECT COUNT(*) FROM users WHERE Name='{newUser.Name}'");
        var exists = await db.Users.AnyAsync(u => u.Name == newUser.Name);
        if (exists)
        {
            Console.WriteLine($"   ❌ User already exists");
            return Results.BadRequest("User already exists");
        }

        Console.WriteLine($"   ✅ Creating new user...");

        // הוסף משתמש חדש
        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        Console.WriteLine($"   ✅ User saved - Id: {newUser.Id}");
        Console.WriteLine($"   🔐 Generating JWT...");

        // יצירת טוקן אחרי הרשמה מוצלחת - משתמש ב-keyBytes מ-Closure
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

        Console.WriteLine($"   ✅ Registration successful! Token: {tokenString.Substring(0, 20)}...");
        return Results.Ok(new { token = tokenString, message = "Registration successful" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ [REGISTER] ERROR: {ex.GetType().Name}");
        Console.WriteLine($"   Message: {ex.Message}");
        if (ex.InnerException != null) Console.WriteLine($"   Inner: {ex.InnerException.Message}");
        Console.WriteLine($"   Stack: {ex.StackTrace}");
        return Results.BadRequest(new { error = "Registration failed", details = ex.Message });
    }
});

// התחברות
app.MapPost("/login", async (PractycodedbContext db, User loginUser) =>
{
    try
    {
        Console.WriteLine($"🔓 [LOGIN] Request - Name: {loginUser.Name}");
        
        if (string.IsNullOrEmpty(loginUser.Name) || string.IsNullOrEmpty(loginUser.Password))
        {
            Console.WriteLine($"   ❌ Validation failed");
            return Results.BadRequest("Name and password are required");
        }

        Console.WriteLine($"   ✅ Validation passed - Searching database...");
        Console.WriteLine($"   🔍 SQL Query: SELECT * FROM users WHERE Name='{loginUser.Name}' AND Password='{loginUser.Password}'");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == loginUser.Name && u.Password == loginUser.Password);
        
        if (user == null)
        {
            Console.WriteLine($"   ❌ User not found");
            return Results.Unauthorized();
        }

        Console.WriteLine($"   ✅ User found (Id:{user.Id}) - Generating JWT...");
        // יצירת הטוקן
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
        
        Console.WriteLine($"   ✅ Login successful!");
        return Results.Ok(new { token = tokenString, message = "Login successful" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ [LOGIN] ERROR: {ex.GetType().Name} - {ex.Message}");
        if (ex.InnerException != null) Console.WriteLine($"   Inner: {ex.InnerException.Message}");
        return Results.BadRequest(new { error = "Login failed", details = ex.Message });
    }
});


// --- Endpoints של משימות (מוגנים ע"י RequireAuthorization) ---

app.MapGet("/items", async (PractycodedbContext db) =>
{
    Console.WriteLine($"📋 [GET /items] Request");
    try
    {
        var items = await db.Tasks.ToListAsync();
        Console.WriteLine($"   ✅ Returned {items.Count} items");
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ❌ ERROR: {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/items", async (PractycodedbContext db, TaskItem newItem) =>
{
    Console.WriteLine($"➕ [POST /items] Request - Name: {newItem.Name}");
    try
    {
        db.Tasks.Add(newItem);
        await db.SaveChangesAsync();
        Console.WriteLine($"   ✅ Task created (Id:{newItem.Id})");
        return Results.Created($"/items/{newItem.Id}", newItem);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ❌ ERROR: {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
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