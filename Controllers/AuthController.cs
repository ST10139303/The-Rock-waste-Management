using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TheRockWasteManagement.Models;

namespace TheRockWasteManagement.Controllers
{
    public class AuthController : Controller
    {
        private readonly FirestoreDb _firestoreDb;

        public AuthController()
        {
            string projectId = "therockwastemanagement";
            _firestoreDb = FirestoreDb.Create(projectId);
        }

        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login()
        {
            // Check if already logged in
            if (IsAuthenticated())
            {
                return RedirectBasedOnRole(HttpContext.Session.GetString("role"));
            }
            return View();
        }

        // GET: /Auth/Register
        [HttpGet]
        public IActionResult Register()
        {
            // Check if already logged in
            if (IsAuthenticated())
            {
                return RedirectBasedOnRole(HttpContext.Session.GetString("role"));
            }
            return View();
        }

        // POST: /Auth/StoreSession - For JavaScript to store session after Firebase auth
        [HttpPost]
        public async Task<IActionResult> StoreSession([FromBody] SessionData data)
        {
            try
            {
                if (data != null && !string.IsNullOrEmpty(data.Uid))
                {
                    // Verify user exists in Firestore and get role
                    var userDoc = await _firestoreDb.Collection("users").Document(data.Uid).GetSnapshotAsync();

                    if (userDoc.Exists)
                    {
                        var userData = userDoc.ConvertTo<Dictionary<string, object>>();
                        var role = userData.ContainsKey("role") ? userData["role"].ToString() : "customer";
                        var email = userData.ContainsKey("email") ? userData["email"].ToString() : data.Email;
                        var status = userData.ContainsKey("status") ? userData["status"].ToString() : "active";
                        var emailVerified = userData.ContainsKey("emailVerified") ?
                            Convert.ToBoolean(userData["emailVerified"]) : false;

                        // Check if account is disabled
                        if (status == "disabled")
                        {
                            return BadRequest(new { success = false, message = "Account has been disabled" });
                        }

                        // Store session (allow login even if email not verified)
                        HttpContext.Session.SetString("uid", data.Uid);
                        HttpContext.Session.SetString("role", role);
                        HttpContext.Session.SetString("email", email ?? "");
                        HttpContext.Session.SetString("IsLoggedIn", "true");
                        HttpContext.Session.SetString("EmailVerified", emailVerified.ToString());

                        return Ok(new
                        {
                            success = true,
                            message = "Session stored",
                            role = role,
                            emailVerified = emailVerified
                        });
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "User not found in database" });
                    }
                }

                return BadRequest(new { success = false, message = "Invalid session data" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: /Auth/GetSession - For JavaScript to get session data
        [HttpGet]
        public IActionResult GetSession()
        {
            var uid = HttpContext.Session.GetString("uid");
            var role = HttpContext.Session.GetString("role");
            var email = HttpContext.Session.GetString("email");
            var isLoggedIn = HttpContext.Session.GetString("IsLoggedIn");
            var emailVerified = HttpContext.Session.GetString("EmailVerified");

            return Ok(new
            {
                isAuthenticated = !string.IsNullOrEmpty(uid) && isLoggedIn == "true",
                uid = uid,
                role = role,
                email = email,
                emailVerified = emailVerified == "True"
            });
        }

        // POST: /Auth/Logout (with anti-forgery token)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            try
            {
                HttpContext.Session.Clear();
                Response.Cookies.Delete(".AspNetCore.Session");

                // Show user type modal after logout
                TempData["ShowUserTypeModal"] = true;
                TempData["Success"] = "You have been successfully logged out.";

                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logout error: {ex.Message}");
                return RedirectToAction("Login");
            }
        }

        // GET: /Auth/Logout (for direct link access)
        [HttpGet("Logout")]
        public IActionResult LogoutGet()
        {
            return Logout();
        }

        // Helper methods
        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("IsLoggedIn") == "true" &&
                   !string.IsNullOrEmpty(HttpContext.Session.GetString("uid"));
        }

        private IActionResult RedirectBasedOnRole(string role)
        {
            return role?.ToLower() switch
            {
                "admin" => RedirectToAction("Dashboard", "Admin"),
                "worker" => RedirectToAction("Dashboard", "Worker"),
                "customer" => RedirectToAction("Dashboard", "Customer"),
                _ => RedirectToAction("Login", "Auth")
            };
        }
    }

    public class SessionData
    {
        public string Uid { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
    }
}