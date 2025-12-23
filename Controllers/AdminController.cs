using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using TheRockWasteManagement.Models;
using System.Net.Mail;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Google.Api;
using Google;
using static Google.Rpc.Context.AttributeContext.Types;

namespace TheRockWasteManagement.Controllers
{
    [Route("Admin")]
    public class AdminController : Controller
    {
        private readonly FirestoreDb _firestoreDb;
        private readonly IConfiguration _config;

        public AdminController(IConfiguration config)
        {
            _config = config;

            // Initialize Firebase if not already initialized
            if (FirebaseApp.DefaultInstance == null)
            {
                var serviceAccountPath = Path.Combine(Directory.GetCurrentDirectory(), "serviceAccountKey.json");

                if (System.IO.File.Exists(serviceAccountPath))
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.FromFile(serviceAccountPath),
                        ProjectId = "therockwastemanagement"
                    });
                }
                else
                {
                    // Fallback to environment variable or default credentials
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.GetApplicationDefault(),
                        ProjectId = "therockwastemanagement"
                    });
                }
            }

            _firestoreDb = FirestoreDb.Create("therockwastemanagement");
        }

        private bool IsAdminAuthenticated()
        {
            var uid = HttpContext.Session.GetString("uid");
            var role = HttpContext.Session.GetString("role");
            return !string.IsNullOrEmpty(uid) && role == "admin";
        }

        // ============ LOGIN METHODS ============
        [HttpGet("Login")]
        public IActionResult Login()
        {
            if (IsAdminAuthenticated())
                return RedirectToAction("Dashboard");
            return View();
        }

        [HttpPost("Login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            try
            {
                email = email?.Trim();
                password = password?.Trim();

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    ModelState.AddModelError(string.Empty, "Email and password are required.");
                    return View();
                }

                var usersRef = _firestoreDb.Collection("users");
                var query = usersRef.WhereEqualTo("email", email.ToLower());
                var snapshot = await query.GetSnapshotAsync();

                if (snapshot.Count == 0)
                {
                    ModelState.AddModelError(string.Empty, "No user found with this email.");
                    return View();
                }

                var doc = snapshot.Documents[0];
                var userData = doc.ConvertTo<Dictionary<string, object>>();
                var uid = doc.Id;

                // Debug: Check all fields
                Console.WriteLine("=== USER DOCUMENT FIELDS ===");
                foreach (var field in userData)
                {
                    Console.WriteLine($"{field.Key}: {field.Value}");
                }

                // Check if password field exists
                if (!userData.TryGetValue("password", out var passwordObj))
                {
                    ModelState.AddModelError(string.Empty, "Password not configured. Please contact administrator.");
                    return View();
                }

                var storedPassword = passwordObj?.ToString();

                if (string.IsNullOrEmpty(storedPassword))
                {
                    ModelState.AddModelError(string.Empty, "Password not set. Please contact administrator.");
                    return View();
                }

                // Compare passwords
                if (storedPassword != password)
                {
                    ModelState.AddModelError(string.Empty, "Incorrect password.");
                    return View();
                }

                // Check account status
                string status = "active";
                if (userData.TryGetValue("status", out var statusObj))
                {
                    status = statusObj?.ToString() ?? "active";
                }

                if (status == "disabled")
                {
                    ModelState.AddModelError(string.Empty, "Your account has been disabled.");
                    return View();
                }

                // Get role
                string role = "customer";
                if (userData.TryGetValue("role", out var roleObj))
                {
                    role = roleObj?.ToString() ?? "customer";
                }

                // Only allow admin login
                if (role != "admin")
                {
                    ModelState.AddModelError(string.Empty, "Admin access required.");
                    return View();
                }

                // Login successful
                HttpContext.Session.SetString("uid", uid);
                HttpContext.Session.SetString("role", role);
                HttpContext.Session.SetString("email", email);
                HttpContext.Session.SetString("IsLoggedIn", "true");

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Login failed: {ex.Message}");
                return View();
            }
        }




        // ============ DASHBOARD ============
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            var model = new AdminDashboardViewModel();

            try
            {
                var usersSnapshot = await _firestoreDb.Collection("users").GetSnapshotAsync();
                var totalCustomers = usersSnapshot.Documents.Count(d =>
                    d.TryGetValue("role", out object role) && role?.ToString() == "customer");

                var bookingsSnapshot = await _firestoreDb.Collection("bookings").GetSnapshotAsync();
                var totalBookings = bookingsSnapshot.Count;

                var paymentsSnapshot = await _firestoreDb.Collection("payments").GetSnapshotAsync();
                decimal totalPayments = 0;

                foreach (var doc in paymentsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();

                    // Try different field names for amount
                    if (data.ContainsKey("Amount") && data["Amount"] != null)
                    {
                        totalPayments += ConvertAmountToDecimal(data["Amount"]);
                    }
                    else if (data.ContainsKey("amount") && data["amount"] != null)
                    {
                        totalPayments += ConvertAmountToDecimal(data["amount"]);
                    }
                }

                var workersSnapshot = await _firestoreDb.Collection("workers").GetSnapshotAsync();
                var totalWorkers = workersSnapshot.Count;

                // NEW: Calculate monthly performance data
                var monthlyPerformance = await CalculateMonthlyPerformance();

                // NEW: Calculate booking status data
                var bookingStatus = await CalculateBookingStatus();

                // NEW: Get recent activities
                var recentActivities = await GetRecentActivities();

                model.TotalCustomers = totalCustomers;
                model.TotalWorkers = totalWorkers;
                model.TotalBookings = totalBookings;
                model.TotalPayments = totalPayments;
                model.MonthlyPerformance = monthlyPerformance ?? new List<MonthlyData>();
                model.BookingStatus = bookingStatus ?? new BookingStatusData();
                model.RecentActivities = recentActivities ?? new List<RecentActivity>();
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading dashboard: {ex.Message}";
                Console.WriteLine($"Dashboard Error: {ex}");
            }

            return View(model);
        }

        // Helper method to convert amount to decimal safely
        private decimal ConvertAmountToDecimal(object amountObj)
        {
            try
            {
                if (amountObj is double d) return Convert.ToDecimal(d);
                else if (amountObj is int i) return i;
                else if (amountObj is long l) return l;
                else if (amountObj is float f) return Convert.ToDecimal(f);
                else if (amountObj is string s && decimal.TryParse(s, out decimal dec)) return dec;
                else return 0;
            }
            catch
            {
                return 0;
            }
        }

        // FIXED: Method to calculate monthly performance
        private async Task<List<MonthlyData>> CalculateMonthlyPerformance()
        {
            try
            {
                var monthlyData = new List<MonthlyData>();
                var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

                var bookingsSnapshot = await _firestoreDb.Collection("bookings").GetSnapshotAsync();
                var paymentsSnapshot = await _firestoreDb.Collection("payments").GetSnapshotAsync();

                for (int i = 0; i < 12; i++)
                {
                    var month = i + 1;
                    var monthBookings = 0;
                    var monthPayments = 0;

                    // Count bookings for this month - check multiple date fields
                    foreach (var doc in bookingsSnapshot.Documents)
                    {
                        var data = doc.ToDictionary();
                        DateTime? date = GetDateFromDocument(data, "BookingDate", "createdAt", "UpdatedAt");

                        if (date.HasValue && date.Value.Month == month && date.Value.Year == DateTime.Now.Year)
                            monthBookings++;
                    }

                    // Count payments for this month - check multiple date fields
                    foreach (var doc in paymentsSnapshot.Documents)
                    {
                        var data = doc.ToDictionary();
                        DateTime? date = GetDateFromDocument(data, "PaymentDate", "createdAt", "UpdatedAt");

                        if (date.HasValue && date.Value.Month == month && date.Value.Year == DateTime.Now.Year)
                            monthPayments++;
                    }

                    monthlyData.Add(new MonthlyData
                    {
                        Month = months[i],
                        Bookings = monthBookings,
                        Payments = monthPayments
                    });
                }

                return monthlyData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating monthly performance: {ex.Message}");
                return new List<MonthlyData>();
            }
        }

        // FIXED: Method to calculate booking status
        private async Task<BookingStatusData> CalculateBookingStatus()
        {
            try
            {
                var bookingsSnapshot = await _firestoreDb.Collection("bookings").GetSnapshotAsync();

                var statusData = new BookingStatusData
                {
                    Completed = 0,
                    Pending = 0,
                    Cancelled = 0,
                    InProgress = 0,
                    ReadingBPS = 0,
                    Approved = 0,
                    Assigned = 0,
                    Rejected = 0
                };

                foreach (var doc in bookingsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    string status = "pending"; // Default

                    if (data.ContainsKey("Status") && data["Status"] != null)
                        status = data["Status"].ToString().ToLower();
                    else if (data.ContainsKey("status") && data["status"] != null)
                        status = data["status"].ToString().ToLower();

                    switch (status)
                    {
                        case "completed":
                        case "done":
                            statusData.Completed++;
                            break;
                        case "pending":
                            statusData.Pending++;
                            break;
                        case "cancelled":
                        case "canceled":
                            statusData.Cancelled++;
                            break;
                        case "in-progress":
                        case "inprogress":
                        case "in progress":
                            statusData.InProgress++;
                            break;
                        case "reading-bps":
                        case "readingbps":
                            statusData.ReadingBPS++;
                            break;
                        case "approved":
                            statusData.Approved++;
                            break;
                        case "assigned":
                            statusData.Assigned++;
                            break;
                        case "rejected":
                            statusData.Rejected++;
                            break;
                        default:
                            statusData.Pending++;
                            break;
                    }
                }

                return statusData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating booking status: {ex.Message}");
                return new BookingStatusData();
            }
        }

        // FIXED: Method to get recent activities
        private async Task<List<RecentActivity>> GetRecentActivities()
        {
            try
            {
                var activities = new List<RecentActivity>();

                // Get recent users (last 5) - check multiple date fields
                var usersSnapshot = await _firestoreDb.Collection("users")
                    .OrderByDescending("createdAt")
                    .Limit(5)
                    .GetSnapshotAsync();

                foreach (var doc in usersSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    DateTime? createdAt = GetDateFromDocument(data, "createdAt", "lastLogin", "CreatedAt");

                    if (createdAt.HasValue)
                    {
                        // Get user name
                        string userName = "New Customer";
                        if (data.ContainsKey("firstName") && data["firstName"] != null)
                        {
                            userName = data["firstName"].ToString();
                            if (data.ContainsKey("lastName") && data["lastName"] != null)
                                userName += " " + data["lastName"].ToString();
                        }
                        else if (data.ContainsKey("name") && data["name"] != null)
                        {
                            userName = data["name"].ToString();
                        }

                        activities.Add(new RecentActivity
                        {
                            Type = "New",
                            Description = $"{userName} registered",
                            Timestamp = createdAt.Value,
                            TimeAgo = GetTimeAgo(createdAt.Value)
                        });
                    }
                }

                // Get recent bookings (last 5) - check multiple date fields
                var bookingsSnapshot = await _firestoreDb.Collection("bookings")
                    .OrderByDescending("createdAt")
                    .Limit(5)
                    .GetSnapshotAsync();

                foreach (var doc in bookingsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    DateTime? createdAt = GetDateFromDocument(data, "createdAt", "BookingDate", "UpdatedAt");

                    if (createdAt.HasValue)
                    {
                        string customerName = data.ContainsKey("CustomerName") && data["CustomerName"] != null
                            ? data["CustomerName"].ToString()
                            : "Customer";

                        string serviceType = data.ContainsKey("ServiceType") && data["ServiceType"] != null
                            ? data["ServiceType"].ToString()
                            : "cleaning";

                        activities.Add(new RecentActivity
                        {
                            Type = "Booking",
                            Description = $"{customerName} booked {serviceType} service",
                            Timestamp = createdAt.Value,
                            TimeAgo = GetTimeAgo(createdAt.Value)
                        });
                    }
                }

                // Get recent payments (last 5)
                var paymentsSnapshot = await _firestoreDb.Collection("payments")
                    .OrderByDescending("PaymentDate")
                    .Limit(5)
                    .GetSnapshotAsync();

                foreach (var doc in paymentsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    DateTime? paymentDate = GetDateFromDocument(data, "PaymentDate", "createdAt", "UpdatedAt");

                    if (paymentDate.HasValue)
                    {
                        string customerName = data.ContainsKey("CustomerName") && data["CustomerName"] != null
                            ? data["CustomerName"].ToString()
                            : "Customer";

                        double amount = 0;
                        if (data.ContainsKey("Amount") && data["Amount"] != null)
                            amount = (double)ConvertAmountToDecimal(data["Amount"]);

                        activities.Add(new RecentActivity
                        {
                            Type = "Payment",
                            Description = $"{customerName} paid R{amount:F2}",
                            Timestamp = paymentDate.Value,
                            TimeAgo = GetTimeAgo(paymentDate.Value)
                        });
                    }
                }

                return activities.OrderByDescending(a => a.Timestamp).Take(8).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting recent activities: {ex.Message}");
                return new List<RecentActivity>();
            }
        }

        // NEW: Helper method to get date from document with multiple possible field names
        private DateTime? GetDateFromDocument(Dictionary<string, object> data, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                if (data.ContainsKey(fieldName) && data[fieldName] != null)
                {
                    var dateObj = data[fieldName];
                    if (dateObj is DateTime dateTime)
                        return dateTime;
                    else if (dateObj is string dateStr && DateTime.TryParse(dateStr, out DateTime parsedDate))
                        return parsedDate;
                    else if (dateObj is Timestamp timestamp)
                        return timestamp.ToDateTime();
                }
            }
            return null;
        }

        // Helper method to calculate time ago
        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalDays >= 30)
                return $"{(int)(timeSpan.TotalDays / 30)} months ago";
            else if (timeSpan.TotalDays >= 7)
                return $"{(int)(timeSpan.TotalDays / 7)} weeks ago";
            else if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} days ago";
            else if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} hours ago";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            else
                return "Just now";
        }

        //Manage admins

        [HttpGet("ManageAdmins")]
        public async Task<IActionResult> ManageAdmins()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            var model = new AdminManagementViewModel();

            try
            {
                // Get all admin users
                var adminUsers = await _firestoreDb.Collection("users")
                    .WhereEqualTo("role", "admin")
                    .GetSnapshotAsync();

                foreach (var doc in adminUsers.Documents)
                {
                    var data = doc.ToDictionary();
                    model.AdminUsers.Add(new AdminUser
                    {
                        Id = doc.Id,
                        Name = data.ContainsKey("name") ? data["name"]?.ToString() : "No Name",
                        Email = data.ContainsKey("email") ? data["email"]?.ToString() : "No Email",
                        Phone = data.ContainsKey("phone") ? data["phone"]?.ToString() : "No Phone",
                        Status = data.ContainsKey("status") ? data["status"]?.ToString() : "active",
                        CreatedAt = GetDateFromDocument(data, "createdAt", "enabledAt") ?? DateTime.Now,
                        LastLogin = GetDateFromDocument(data, "lastLogin", "disabledAt")
                    });
                }

                model.AdminUsers = model.AdminUsers.OrderByDescending(a => a.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading admin users: {ex.Message}";
            }

            return View(model);
        }
        [HttpPost("AddAdmin")]
        public async Task<IActionResult> AddAdmin(AdminManagementViewModel model)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(model.NewAdminEmail) || string.IsNullOrEmpty(model.NewAdminName))
                {
                    TempData["Error"] = "Email and Name are required";
                    return RedirectToAction("ManageAdmins");
                }

                // Check if user already exists in Firestore
                var existingUser = await _firestoreDb.Collection("users")
                    .WhereEqualTo("email", model.NewAdminEmail.Trim().ToLower())
                    .GetSnapshotAsync();

                if (existingUser.Documents.Count > 0)
                {
                    TempData["Error"] = "A user with this email already exists";
                    return RedirectToAction("ManageAdmins");
                }

                // Generate password if not provided
                var password = model.NewAdminPassword ?? GenerateTemporaryPassword();

                // ✅ CREATE FIREBASE AUTH USER
                var auth = FirebaseAuth.DefaultInstance;
                var userArgs = new UserRecordArgs
                {
                    Email = model.NewAdminEmail.Trim().ToLower(),
                    EmailVerified = true, // Auto-verify admin emails
                    Password = password,
                    DisplayName = model.NewAdminName.Trim()
                };

                UserRecord userRecord;
                try
                {
                    userRecord = await auth.CreateUserAsync(userArgs);
                }
                catch (FirebaseAuthException ex)
                {
                    if (ex.Message.Contains("email already exists"))
                    {
                        // User exists in Auth but not in Firestore - get the existing user
                        userRecord = await auth.GetUserByEmailAsync(model.NewAdminEmail.Trim().ToLower());
                    }
                    else
                    {
                        throw;
                    }
                }

                // ✅ CREATE FIRESTORE DOCUMENT
                var adminData = new Dictionary<string, object>
                {
                    ["name"] = model.NewAdminName.Trim(),
                    ["email"] = model.NewAdminEmail.Trim().ToLower(),
                    ["phone"] = model.NewAdminPhone?.Trim() ?? "",
                    ["role"] = "admin",
                    ["status"] = "active",
                    ["password"] = password, // Keep for backup/AdminController login
                    ["createdAt"] = FieldValue.ServerTimestamp,
                    ["enabledAt"] = FieldValue.ServerTimestamp,
                    ["lastLogin"] = FieldValue.ServerTimestamp,
                    ["uid"] = userRecord.Uid, // Store Firebase UID
                    ["emailVerified"] = true
                };

                await _firestoreDb.Collection("users").Document(userRecord.Uid).SetAsync(adminData);

                TempData["Success"] = $"Admin {model.NewAdminName} added successfully! Password: {password}";

                // Log this activity
                await LogAdminActivity($"Added new admin: {model.NewAdminEmail}");

                return RedirectToAction("ManageAdmins");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error adding admin: {ex.Message}";
                return RedirectToAction("ManageAdmins");
            }
        }

        [HttpPost("ToggleAdminStatus")]
        public async Task<IActionResult> ToggleAdminStatus(string adminId, string currentStatus)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var newStatus = currentStatus == "active" ? "inactive" : "active";
                var updateData = new Dictionary<string, object>
                {
                    ["status"] = newStatus
                };

                if (newStatus == "active")
                {
                    updateData["enabledAt"] = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                else
                {
                    updateData["disabledAt"] = Timestamp.FromDateTime(DateTime.UtcNow);
                }

                await _firestoreDb.Collection("users").Document(adminId).UpdateAsync(updateData);

                TempData["Success"] = $"Admin status updated to {newStatus}";
                await LogAdminActivity($"Changed admin status to {newStatus} for user {adminId}");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating admin status: {ex.Message}";
            }

            return RedirectToAction("ManageAdmins");
        }

        // Helper method to generate temporary password
        private string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // Helper method to log admin activities
        private async Task LogAdminActivity(string activity)
        {
            try
            {
                var activityData = new Dictionary<string, object>
                {
                    ["activity"] = activity,
                    ["adminId"] = GetCurrentAdminId(),
                    ["timestamp"] = Timestamp.FromDateTime(DateTime.UtcNow),
                    ["type"] = "admin_management"
                };

                await _firestoreDb.Collection("admin_activities").AddAsync(activityData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging admin activity: {ex.Message}");
            }
        }

        // Helper method to get current admin ID (you'll need to implement this based on your auth system)
        private string GetCurrentAdminId()
        {
            // This depends on your authentication implementation
            // Return the current logged-in admin's user ID
            return "current-admin-id"; // Replace with actual implementation
        }
        // ============ CUSTOMER MANAGEMENT ============
        [HttpGet("ViewCustomers")]
        public async Task<IActionResult> ViewCustomers()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // Get all users (customers, admins, and workers)
                var usersSnapshot = await _firestoreDb.Collection("users").GetSnapshotAsync();

                // Get all workers from the workers collection
                var workersSnapshot = await _firestoreDb.Collection("workers").GetSnapshotAsync();

                var allUsers = new List<Dictionary<string, object>>();

                // Process regular users (customers and admins)
                foreach (var doc in usersSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    data["Id"] = doc.Id;
                    data["UserType"] = "User"; // Mark as regular user

                    // Determine role for display
                    if (data.ContainsKey("role"))
                    {
                        data["DisplayRole"] = data["role"].ToString().ToUpper();
                    }
                    else
                    {
                        data["DisplayRole"] = "CUSTOMER";
                    }

                    allUsers.Add(data);
                }

                // Process workers (from workers collection)
                foreach (var doc in workersSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    data["Id"] = doc.Id;
                    data["UserType"] = "Worker"; // Mark as worker
                    data["DisplayRole"] = "WORKER";

                    // Map worker fields to match user structure for the view
                    if (data.ContainsKey("Name") && !data.ContainsKey("name"))
                        data["name"] = data["Name"];
                    if (data.ContainsKey("Email") && !data.ContainsKey("email"))
                        data["email"] = data["Email"];
                    if (data.ContainsKey("Phone") && !data.ContainsKey("phone"))
                        data["phone"] = data["Phone"];

                    allUsers.Add(data);
                }

                return View(allUsers);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading users: {ex.Message}";
                return View(new List<Dictionary<string, object>>());
            }
        }


        [HttpPost("DeleteUser")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("users").Document(userId).DeleteAsync();
                TempData["Success"] = "User deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting user: {ex.Message}";
            }

            return RedirectToAction("ViewCustomers");
        }

        [HttpPost("DisableUser")]
        public async Task<IActionResult> DisableUser(string userId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("users").Document(userId).UpdateAsync(new Dictionary<string, object>
                {
                    { "status", "disabled" },
                    { "disabledAt", DateTime.UtcNow }
                });

                TempData["Success"] = "User disabled successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error disabling user: {ex.Message}";
            }

            return RedirectToAction("ViewCustomers");
        }

        [HttpPost("EnableUser")]
        public async Task<IActionResult> EnableUser(string userId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("users").Document(userId).UpdateAsync(new Dictionary<string, object>
                {
                    { "status", "active" },
                    { "enabledAt", DateTime.UtcNow }
                });

                TempData["Success"] = "User enabled successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error enabling user: {ex.Message}";
            }

            return RedirectToAction("ViewCustomers");
        }

        // ============ WORKER MANAGEMENT ============
        [HttpGet("ViewWorkers")]
        public async Task<IActionResult> ViewWorkers()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var workers = new List<WorkerModel>();
                var snapshot = await _firestoreDb.Collection("workers").GetSnapshotAsync();

                foreach (var doc in snapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    workers.Add(new WorkerModel
                    {
                        Id = doc.Id,
                        Name = data.ContainsKey("Name") ? data["Name"].ToString() : "",
                        Phone = data.ContainsKey("Phone") ? data["Phone"].ToString() : "",
                        Email = data.ContainsKey("Email") ? data["Email"].ToString() : "",
                        IsActive = data.ContainsKey("IsActive") ? Convert.ToBoolean(data["IsActive"]) : true
                    });
                }

                return View(workers);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading workers: {ex.Message}";
                return View(new List<WorkerModel>());
            }
        }
        [HttpGet("AddWorker")]
        public IActionResult AddWorker()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            return View(new WorkerModel());
        }

        [HttpPost("AddWorker")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddWorker(WorkerModel worker)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            // Manual validation
            if (string.IsNullOrEmpty(worker.Name) || worker.Name.Length < 2)
            {
                ModelState.AddModelError("Name", "Name must be at least 2 characters long");
            }

            if (string.IsNullOrEmpty(worker.Phone) || !System.Text.RegularExpressions.Regex.IsMatch(worker.Phone, @"^\+[1-9]\d{1,14}$"))
            {
                ModelState.AddModelError("Phone", "Please enter a valid phone number with country code");
            }

            if (string.IsNullOrEmpty(worker.Email) || !IsValidEmail(worker.Email))
            {
                ModelState.AddModelError("Email", "Please enter a valid email address");
            }

            if (!ModelState.IsValid)
            {
                return View(worker);
            }

            try
            {
                // Check if worker with same email already exists
                var existingWorkerQuery = _firestoreDb.Collection("workers").WhereEqualTo("Email", worker.Email);
                var existingSnapshot = await existingWorkerQuery.GetSnapshotAsync();

                if (existingSnapshot.Count > 0)
                {
                    ModelState.AddModelError("Email", "A worker with this email already exists");
                    return View(worker);
                }

                var data = new Dictionary<string, object>
        {
            { "Name", worker.Name.Trim() },
            { "Phone", worker.Phone.Trim() },
            { "Email", worker.Email.Trim().ToLower() },
            { "IsActive", true },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

                await _firestoreDb.Collection("workers").AddAsync(data);
                TempData["Success"] = $"Worker '{worker.Name}' added successfully!";

                return RedirectToAction("ViewWorkers");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error adding worker: {ex.Message}";
                return View(worker);
            }
        }

        // Helper method for email validation
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        [HttpPost("DeleteWorker")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWorker(string workerId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("workers").Document(workerId).DeleteAsync();
                TempData["Success"] = "Worker deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting worker: {ex.Message}";
            }

            return RedirectToAction("ViewWorkers");
        }

        [HttpGet("EditWorker/{workerId}")]
        public async Task<IActionResult> EditWorker(string workerId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var workerDoc = await _firestoreDb.Collection("workers").Document(workerId).GetSnapshotAsync();

                if (!workerDoc.Exists)
                {
                    TempData["Error"] = "Worker not found!";
                    return RedirectToAction("ViewWorkers");
                }

                var data = workerDoc.ToDictionary();
                var worker = new WorkerModel
                {
                    Id = workerDoc.Id,
                    Name = data.ContainsKey("Name") ? data["Name"].ToString() : "",
                    Phone = data.ContainsKey("Phone") ? data["Phone"].ToString() : "",
                    Email = data.ContainsKey("Email") ? data["Email"].ToString() : "",
                    IsActive = data.ContainsKey("IsActive") ? Convert.ToBoolean(data["IsActive"]) : true
                };

                return View(worker);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error loading worker: {ex.Message}";
                return RedirectToAction("ViewWorkers");
            }
        }

        [HttpPost("EditWorker/{workerId}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditWorker(string workerId, WorkerModel worker)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            // Clear ModelState for Id field since it's not in the form
            ModelState.Remove("Id");

            if (!ModelState.IsValid)
            {
                return View(worker);
            }

            try
            {
                // Check if another worker already has this email (excluding current worker)
                var existingWorkerQuery = _firestoreDb.Collection("workers")
                    .WhereEqualTo("Email", worker.Email.Trim().ToLower());
                var existingSnapshot = await existingWorkerQuery.GetSnapshotAsync();

                var duplicateWorker = existingSnapshot.Documents
                    .FirstOrDefault(doc => doc.Id != workerId);

                if (duplicateWorker != null)
                {
                    ModelState.AddModelError("Email", "A worker with this email already exists");
                    return View(worker);
                }

                var updateData = new Dictionary<string, object>
        {
            { "Name", worker.Name.Trim() },
            { "Phone", worker.Phone.Trim() },
            { "Email", worker.Email.Trim().ToLower() },
            { "UpdatedAt", DateTime.UtcNow }
        };

                await _firestoreDb.Collection("workers").Document(workerId).UpdateAsync(updateData);
                TempData["Success"] = $"Worker '{worker.Name}' updated successfully!";

                return RedirectToAction("ViewWorkers");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating worker: {ex.Message}";
                return View(worker);
            }
        }
        // ============ BOOKING MANAGEMENT ============
        [HttpGet("ViewBookings")]
        public async Task<IActionResult> ViewBookings()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var bookingsSnapshot = await _firestoreDb.Collection("bookings").GetSnapshotAsync();
                var bookingsList = new List<dynamic>();

                foreach (var doc in bookingsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();

                    string customerName = "Unknown Customer";
                    if (data.ContainsKey("CustomerName"))
                    {
                        customerName = data["CustomerName"].ToString();
                    }

                    DateTime bookingDate = DateTime.MinValue;
                    if (data.ContainsKey("BookingDate") && data["BookingDate"] is Timestamp timestamp)
                    {
                        bookingDate = timestamp.ToDateTime();
                    }

                    // Handle double to decimal conversion safely
                    double estimatedPrice = 0;
                    double finalPrice = 0;

                    if (data.ContainsKey("EstimatedPrice"))
                    {
                        if (data["EstimatedPrice"] is double estDouble)
                            estimatedPrice = estDouble;
                        else if (data["EstimatedPrice"] is int estInt)
                            estimatedPrice = estInt;
                    }

                    if (data.ContainsKey("FinalPrice"))
                    {
                        if (data["FinalPrice"] is double finDouble)
                            finalPrice = finDouble;
                        else if (data["FinalPrice"] is int finInt)
                            finalPrice = finInt;
                    }

                    var booking = new
                    {
                        BookingId = doc.Id,
                        CustomerName = customerName,
                        Address = data.ContainsKey("BookingAddress") ? data["BookingAddress"].ToString() : "No address",
                        Date = bookingDate,
                        TimeSlot = data.ContainsKey("PreferredTime") ? data["PreferredTime"].ToString() : "Not specified",
                        Status = data.ContainsKey("Status") ? data["Status"].ToString().ToLower() : "pending",
                        ServiceType = data.ContainsKey("ServiceType") ? data["ServiceType"].ToString() : "unknown",
                        EstimatedPrice = estimatedPrice,
                        FinalPrice = finalPrice,
                        IsPriceSet = data.ContainsKey("IsPriceSet") && (bool)data["IsPriceSet"],
                        PaymentStatus = data.ContainsKey("PaymentStatus") ? data["PaymentStatus"].ToString() : "pending",
                        SpecialRequest = data.ContainsKey("SpecialRequest") ? data["SpecialRequest"].ToString() : ""
                    };

                    bookingsList.Add(booking);
                }

                bookingsList = bookingsList.OrderByDescending(b => b.Date).ToList();
                return View(bookingsList);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error loading bookings: {ex.Message}";
                return View(new List<dynamic>());
            }
        }
        [HttpPost("UpdateBookingStatus")]
        public async Task<IActionResult> UpdateBookingStatus(string bookingId, string newStatus)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
                {
                    { "Status", newStatus.ToLower() },
                    { "UpdatedAt", DateTime.UtcNow }
                });

                TempData["Success"] = $"Booking status updated to {newStatus} successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating booking status: {ex.Message}";
            }

            return RedirectToAction("ViewBookings");
        }

        [HttpPost("DeleteBooking")]
        public async Task<IActionResult> DeleteBooking(string bookingId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("bookings").Document(bookingId).DeleteAsync();
                TempData["Success"] = "Booking deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting booking: {ex.Message}";
            }

            return RedirectToAction("ViewBookings");
        }

        // ============ PAYMENT HISTORY ============
        [HttpGet("PaymentHistory")]
        public async Task<IActionResult> PaymentHistory()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var paymentsSnapshot = await _firestoreDb.Collection("payments").GetSnapshotAsync();
                var payments = new List<PaymentModel>();

                foreach (var doc in paymentsSnapshot.Documents)
                {
                    if (!doc.Exists) continue;

                    var data = doc.ToDictionary();
                    if (data == null) continue;

                    DateTime paymentDate = DateTime.UtcNow;
                    if (data.ContainsKey("PaymentDate") && data["PaymentDate"] != null && data["PaymentDate"] is Timestamp timestamp)
                    {
                        paymentDate = timestamp.ToDateTime();
                    }

                    double amount = 0;
                    if (data.ContainsKey("Amount") && data["Amount"] != null)
                    {
                        var amountValue = data["Amount"];
                        if (amountValue is double d)
                            amount = d;
                        else if (amountValue is int i)
                            amount = i;
                        else if (amountValue is long l)
                            amount = l;
                        else if (amountValue is float f)
                            amount = f;
                        else if (amountValue is string s && double.TryParse(s, out double parsed))
                            amount = parsed;
                    }

                    payments.Add(new PaymentModel
                    {
                        Id = doc.Id,
                        CustomerName = data.ContainsKey("CustomerName") && data["CustomerName"] != null ?
                            data["CustomerName"].ToString() : "Unknown Customer",
                        Description = data.ContainsKey("Description") && data["Description"] != null ?
                            data["Description"].ToString() : "No description",
                        Amount = amount,
                        PaymentMethod = data.ContainsKey("PaymentMethod") && data["PaymentMethod"] != null ?
                            data["PaymentMethod"].ToString() : "Unknown",
                        Reference = data.ContainsKey("Reference") && data["Reference"] != null ?
                            data["Reference"].ToString() : "",
                        PaymentDate = paymentDate,
                        Status = data.ContainsKey("Status") && data["Status"] != null ?
                            data["Status"].ToString() : "pending",
                        BookingId = data.ContainsKey("BookingId") && data["BookingId"] != null ?
                            data["BookingId"].ToString() : null,
                        CustomerId = data.ContainsKey("CustomerId") && data["CustomerId"] != null ?
                            data["CustomerId"].ToString() : null
                    });
                }

                // Order by payment date descending (newest first)
                payments = payments.OrderByDescending(p => p.PaymentDate).ToList();

                return View(payments);
            }
            catch (Exception ex)
            {
                // Log the full error for debugging
                Console.WriteLine($"Payment History Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                TempData["Error"] = $"Error loading payments: {ex.Message}";
                return View(new List<PaymentModel>());
            }
        }

        // ============ ASSIGN WORKER ============
        [HttpGet("AssignWorker")]
        public async Task<IActionResult> AssignWorker()
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                var workersSnapshot = await _firestoreDb.Collection("workers").GetSnapshotAsync();
                var workers = workersSnapshot.Documents.Select(doc =>
                {
                    var data = doc.ToDictionary();
                    return new WorkerModel
                    {
                        Id = doc.Id,
                        Name = data.ContainsKey("Name") ? data["Name"].ToString() : "Unknown",
                        Email = data.ContainsKey("Email") ? data["Email"].ToString() : ""
                    };
                }).ToList();

                var bookingsSnapshot = await _firestoreDb.Collection("bookings")
                    .WhereIn("Status", new[] { "pending", "approved" })
                    .GetSnapshotAsync();

                var bookings = bookingsSnapshot.Documents.Select(doc =>
                {
                    var data = doc.ToDictionary();
                    DateTime bookingDate = DateTime.MinValue;
                    if (data.ContainsKey("BookingDate") && data["BookingDate"] is Timestamp timestamp)
                    {
                        bookingDate = timestamp.ToDateTime();
                    }

                    return new BookingModel
                    {
                        Id = doc.Id,
                        BookingAddress = data.ContainsKey("BookingAddress") ? data["BookingAddress"].ToString() :
                                      data.ContainsKey("address") ? data["address"].ToString() : "Unknown",
                        BookingDate = bookingDate,
                        Status = data.ContainsKey("Status") ? data["Status"].ToString() : "pending"
                    };
                }).ToList();

                // Load current assignments with proper data
                var assignmentsSnapshot = await _firestoreDb.Collection("assignments").GetSnapshotAsync();
                var assignments = new List<AssignmentViewModel>();

                foreach (var doc in assignmentsSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    var assignment = new AssignmentViewModel
                    {
                        Id = doc.Id,
                        WorkerId = data.ContainsKey("AssignedWorker") ? data["AssignedWorker"].ToString() : "",
                        BookingId = data.ContainsKey("BookingId") ? data["BookingId"].ToString() : "",
                        WorkerStatus = data.ContainsKey("WorkerStatus") ? data["WorkerStatus"].ToString() : "Pending",
                        BookingStatus = data.ContainsKey("Status") ? data["Status"].ToString() : "assigned"
                    };

                    // Get worker name
                    if (!string.IsNullOrEmpty(assignment.WorkerId))
                    {
                        var workerDoc = await _firestoreDb.Collection("workers").Document(assignment.WorkerId).GetSnapshotAsync();
                        if (workerDoc.Exists)
                        {
                            var workerData = workerDoc.ToDictionary();
                            assignment.WorkerName = workerData.ContainsKey("Name") ? workerData["Name"].ToString() : "Unknown Worker";
                        }
                    }

                    // Get booking address
                    if (!string.IsNullOrEmpty(assignment.BookingId))
                    {
                        var bookingDoc = await _firestoreDb.Collection("bookings").Document(assignment.BookingId).GetSnapshotAsync();
                        if (bookingDoc.Exists)
                        {
                            var bookingData = bookingDoc.ToDictionary();
                            assignment.BookingAddress = bookingData.ContainsKey("BookingAddress") ?
                                bookingData["BookingAddress"].ToString() : "Unknown Address";

                            if (bookingData.ContainsKey("BookingDate") && bookingData["BookingDate"] is Timestamp timestamp)
                            {
                                assignment.Date = timestamp.ToDateTime();
                            }
                        }
                    }

                    assignments.Add(assignment);
                }

                var model = new AssignWorkerPageModel
                {
                    Workers = workers,
                    Bookings = bookings,
                    Assignments = assignments,
                    CurrentAssignments = assignments
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error loading data: {ex.Message}";
                return View(new AssignWorkerPageModel());
            }
        }

        [HttpPost("AssignWorker")]
        public async Task<IActionResult> AssignWorker(string workerId, string bookingId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // Update booking collection
                await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
        {
            { "AssignedWorker", workerId },
            { "Status", "assigned" },
            { "UpdatedAt", DateTime.UtcNow }
        });

                // Create or update assignment collection
                var assignmentData = new Dictionary<string, object>
        {
            { "AssignedWorker", workerId },
            { "BookingId", bookingId },
            { "Status", "assigned" },
            { "WorkerStatus", "Pending" },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

                // Check if assignment already exists
                var existingAssignment = await _firestoreDb.Collection("assignments")
                    .WhereEqualTo("BookingId", bookingId)
                    .Limit(1)
                    .GetSnapshotAsync();

                if (existingAssignment.Documents.Any())
                {
                    // Update existing assignment
                    await _firestoreDb.Collection("assignments")
                        .Document(existingAssignment.Documents[0].Id)
                        .UpdateAsync(assignmentData);
                }
                else
                {
                    // Create new assignment
                    await _firestoreDb.Collection("assignments").AddAsync(assignmentData);
                }

                TempData["Success"] = "Worker assigned successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error assigning worker: {ex.Message}";
            }

            return RedirectToAction("AssignWorker");
        }

        [HttpPost("CompleteAssignment")]
        public async Task<IActionResult> CompleteAssignment(string assignmentId, string bookingId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // Update assignment to mark as fully completed
                await _firestoreDb.Collection("assignments").Document(assignmentId).UpdateAsync(new Dictionary<string, object>
        {
            { "IsFullyCompleted", true },
            { "CompletedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        });

                // Update booking status to completed
                await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
        {
            { "Status", "completed" },
            { "WorkerStatus", "Completed" },
            { "UpdatedAt", DateTime.UtcNow }
        });

                TempData["Success"] = "Assignment marked as completed successfully! It has been moved to completed tasks.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error completing assignment: {ex.Message}";
            }

            return RedirectToAction("AssignWorker");
        }

        [HttpPost("DeleteWorkerUser")]
        public async Task<IActionResult> DeleteWorkerUser(string workerId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("workers").Document(workerId).DeleteAsync();
                TempData["Success"] = "Worker deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting worker: {ex.Message}";
            }

            return RedirectToAction("ViewCustomers");
        }

        [HttpPost("ToggleWorkerStatus")]
        public async Task<IActionResult> ToggleWorkerStatus(string workerId, bool isActive)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("workers").Document(workerId).UpdateAsync(new Dictionary<string, object>
        {
            { "IsActive", isActive },
            { "UpdatedAt", DateTime.UtcNow }
        });

                TempData["Success"] = $"Worker {(isActive ? "activated" : "deactivated")} successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating worker status: {ex.Message}";
            }

            return RedirectToAction("ViewCustomers");
        }

        // In AdminController.cs
        [HttpPost("UpdateWorkerStatus")]
        public async Task<IActionResult> UpdateWorkerStatus(string assignmentId, string workerStatus, string bookingId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // Update assignment collection
                await _firestoreDb.Collection("assignments").Document(assignmentId).UpdateAsync(new Dictionary<string, object>
        {
            { "WorkerStatus", workerStatus },
            { "UpdatedAt", DateTime.UtcNow }
        });

                // Also update booking collection to keep them in sync
                await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
        {
            { "WorkerStatus", workerStatus },
            { "UpdatedAt", DateTime.UtcNow }
        });

                TempData["Success"] = $"Worker status updated to {workerStatus} successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating worker status: {ex.Message}";
            }

            return RedirectToAction("AssignWorker");
        }

        [HttpPost("DeleteAssignment")]
        public async Task<IActionResult> DeleteAssignment(string assignmentId)
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                // First, get the assignment to find the related booking and worker
                var assignmentDoc = await _firestoreDb.Collection("assignments").Document(assignmentId).GetSnapshotAsync();

                if (assignmentDoc.Exists)
                {
                    var assignmentData = assignmentDoc.ToDictionary();

                    // Remove worker assignment from the booking
                    if (assignmentData.ContainsKey("BookingId"))
                    {
                        var bookingId = assignmentData["BookingId"].ToString();
                        await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
                {
                    { "AssignedWorker", null },
                    { "WorkerStatus", null },
                    { "Status", "approved" }, // Reset to approved
                    { "UpdatedAt", DateTime.UtcNow }
                });
                    }

                    // Delete the assignment document
                    await _firestoreDb.Collection("assignments").Document(assignmentId).DeleteAsync();

                    TempData["Success"] = "Assignment deleted successfully! The worker will no longer see this assignment.";
                }
                else
                {
                    TempData["Error"] = "Assignment not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting assignment: {ex.Message}";
            }

            return RedirectToAction("AssignWorker");
        }
        [HttpPost("SetBookingPrice")]
        public async Task<IActionResult> SetBookingPrice(string bookingId, double finalPrice) // Change to double
        {
            if (!IsAdminAuthenticated())
                return RedirectToAction("Login");

            try
            {
                await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
        {
            { "FinalPrice", finalPrice }, // Now using double
            { "IsPriceSet", true },
            { "UpdatedAt", DateTime.UtcNow }
        });

                TempData["Success"] = $"Price set to R{finalPrice:F2} successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error setting price: {ex.Message}";
            }

            return RedirectToAction("ViewBookings");
        }

        [HttpPost("Logout")]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            try
            {
                // Clear all session variables
                HttpContext.Session.Clear();

                // Optional: Expire the authentication cookie if you're using cookie auth
                Response.Cookies.Delete(".AspNetCore.Session");

                // Optional: If using any other authentication schemes, sign out here
                // await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                TempData["Success"] = "You have been successfully logged out.";
                TempData["ShowUserTypeModal"] = true; // This triggers the modal
            }
            catch (Exception ex)
            {
                // Log the error but still proceed with logout
                Console.WriteLine($"Error during logout: {ex.Message}");
            }

            return RedirectToAction("Login", "Admin");
        }

        // GET version for direct link access (optional)
        [HttpGet("Logout")]
        public IActionResult LogoutGet()
        {
            return Logout();
        }
    }
}