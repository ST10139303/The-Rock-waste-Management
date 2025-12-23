using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;
using TheRockWasteManagement.Models;
using static Google.Cloud.Firestore.V1.StructuredQuery.Types;

namespace TheRockWasteManagement.Controllers
{
    public class WorkerController : Controller
    {
        private readonly FirestoreDb _firestoreDb;

        public WorkerController()
        {
            _firestoreDb = FirestoreDb.Create("therockwastemanagement"); // Your Firebase project ID
        }

        // ==================== LOGIN ====================
        [HttpGet]
        public IActionResult Login()
        {
            // Check if already logged in
            if (IsWorkerAuthenticated())
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(WorkerLoginModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = "Please fill in all fields correctly.";
                return View(model);
            }

            try
            {
                // Query workers collection with correct field names
                var workerSnapshot = await _firestoreDb.Collection("workers")
                    .WhereEqualTo("Email", model.Email.Trim().ToLower())
                    .WhereEqualTo("Phone", model.Phone.Trim())
                    .WhereEqualTo("IsActive", true)  // Changed from "Approved" to "IsActive"
                    .GetSnapshotAsync();

                if (workerSnapshot.Count == 0)
                {
                    ViewBag.Error = "Invalid credentials or account is not active.";
                    return View(model);
                }

                var workerDoc = workerSnapshot.Documents[0];
                var workerData = workerDoc.ToDictionary();

                // Get worker details
                var workerName = workerData.ContainsKey("Name") ? workerData["Name"].ToString() : "Worker";
                var workerId = workerDoc.Id;

                // Set session variables
                HttpContext.Session.SetString("WorkerId", workerId);
                HttpContext.Session.SetString("WorkerEmail", model.Email);
                HttpContext.Session.SetString("WorkerName", workerName);
                HttpContext.Session.SetString("WorkerRole", "worker");
                HttpContext.Session.SetString("IsLoggedIn", "true");

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Login failed: {ex.Message}";
                return View(model);
            }
        }

        // Helper method to check if worker is authenticated
        private bool IsWorkerAuthenticated()
        {
            return HttpContext.Session.GetString("IsLoggedIn") == "true" &&
                   HttpContext.Session.GetString("WorkerRole") == "worker" &&
                   !string.IsNullOrEmpty(HttpContext.Session.GetString("WorkerId"));
        }

        // ==================== DASHBOARD ====================
        public async Task<IActionResult> Dashboard()
        {
            var email = HttpContext.Session.GetString("WorkerEmail");
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login", "Worker");
            }

            // Get the worker's Firestore document ID by email
            var workerSnapshot = await _firestoreDb.Collection("workers")
                .WhereEqualTo("Email", email)
                .Limit(1)
                .GetSnapshotAsync();

            if (!workerSnapshot.Any())
            {
                ViewBag.WorkerEmail = email;
                return View(new List<BookingModel>());
            }

            var workerId = workerSnapshot.First().Id;

            // Get ACTIVE assignments for this worker (exclude fully completed ones)
            var assignmentsSnapshot = await _firestoreDb.Collection("assignments")
                .WhereEqualTo("AssignedWorker", workerId)
                .GetSnapshotAsync();

            var bookings = new List<BookingModel>();

            foreach (var assignmentDoc in assignmentsSnapshot.Documents)
            {
                var assignmentData = assignmentDoc.ToDictionary();

                // Skip assignments that are marked as fully completed by admin
                if (assignmentData.ContainsKey("IsFullyCompleted") &&
                    assignmentData["IsFullyCompleted"] is bool isCompleted && isCompleted)
                {
                    continue; // Skip completed assignments
                }

                if (assignmentData.ContainsKey("BookingId"))
                {
                    var bookingId = assignmentData["BookingId"].ToString();
                    var bookingDoc = await _firestoreDb.Collection("bookings").Document(bookingId).GetSnapshotAsync();

                    if (bookingDoc.Exists)
                    {
                        var bookingData = bookingDoc.ToDictionary();

                        var booking = new BookingModel
                        {
                            BookingId = bookingId,
                            AssignmentId = assignmentDoc.Id,
                            Address = bookingData.ContainsKey("BookingAddress") ?
                                bookingData["BookingAddress"].ToString() : "N/A",
                            BookingDate = bookingData.ContainsKey("BookingDate") &&
                                bookingData["BookingDate"] is Timestamp timestamp ?
                                timestamp.ToDateTime() : DateTime.MinValue,
                            TimeSlot = bookingData.ContainsKey("PreferredTime") ?
                                bookingData["PreferredTime"].ToString() : "Not specified",
                            ServiceType = bookingData.ContainsKey("ServiceType") ?
                                bookingData["ServiceType"].ToString() : "General",
                            CustomerName = bookingData.ContainsKey("CustomerName") ?
                                bookingData["CustomerName"].ToString() : "Customer",
                            WorkerStatus = assignmentData.ContainsKey("WorkerStatus") ?
                                assignmentData["WorkerStatus"].ToString() : "Pending"
                        };

                        bookings.Add(booking);
                    }
                }
            }

            ViewBag.WorkerEmail = email;
            ViewBag.WorkerId = workerId;
            return View(bookings);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAssignmentStatus(string assignmentId, string status, string bookingId)
        {
            var email = HttpContext.Session.GetString("WorkerEmail");
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login", "Worker");
            }

            try
            {
                // Update assignment status
                await _firestoreDb.Collection("assignments").Document(assignmentId).UpdateAsync(new Dictionary<string, object>
        {
            { "WorkerStatus", status },
            { "UpdatedAt", DateTime.UtcNow }
        });

                // Also update the booking's worker status for admin visibility
                if (!string.IsNullOrEmpty(bookingId))
                {
                    await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
            {
                { "WorkerStatus", status },
                { "UpdatedAt", DateTime.UtcNow }
            });
                }

                TempData["Success"] = $"Status updated to {status} successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating status: {ex.Message}";
            }

            return RedirectToAction("Dashboard");
        }


        // ==================== FEEDBACK ====================
        [HttpPost]
public async Task<IActionResult> SubmitFeedback(string bookingId, string feedback)
{
    if (string.IsNullOrEmpty(bookingId) || string.IsNullOrEmpty(feedback))
    {
        TempData["Error"] = "Booking or feedback is missing.";
        return RedirectToAction("Dashboard");
    }

    // 1. Update booking
    var bookingRef = _firestoreDb.Collection("bookings").Document(bookingId);
    await bookingRef.UpdateAsync("WorkerStatus", feedback);

    // 2. Update assignment linked to this booking
    var assignmentSnapshot = await _firestoreDb.Collection("assignments")
        .WhereEqualTo("BookingId", bookingId)
        .GetSnapshotAsync();

    foreach (var doc in assignmentSnapshot.Documents)
    {
        await doc.Reference.UpdateAsync("WorkerStatus", feedback);
    }

    TempData["Success"] = "Feedback submitted successfully!";
    return RedirectToAction("Dashboard");
}


        [HttpPost]
        public async Task<IActionResult> DeleteAssignment(string assignmentId)
        {
            if (string.IsNullOrEmpty(assignmentId))
            {
                TempData["Error"] = "Invalid assignment ID.";
                return RedirectToAction("AssignWorker");
            }

            var db = FirestoreDb.Create("the-rock-waste-management");

            try
            {
                await db.Collection("assignments").Document(assignmentId).DeleteAsync();
                TempData["Success"] = "Assignment deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting assignment: {ex.Message}";
            }

            return RedirectToAction("AssignWorker");
        }


        // ==================== FIREBASE AUTH (Optional) ====================
        private async Task<string> SignInWorkerAsync(string email, string password)
        {
            var client = new HttpClient();
            var apiKey = "AIzaSyCBy2OTgcZ6J1CXE_M2Z6ZgYmQTo1k4Lcc"; // 🔒 Replace with secure config

            var payload = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            var response = await client.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            );

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Firebase Auth failed: " + json);
            }

            dynamic result = JsonConvert.DeserializeObject(json);
            return result.idToken;
        }

        [HttpGet("CompletedTasks")]
        public async Task<IActionResult> CompletedTasks()
        {
            var email = HttpContext.Session.GetString("WorkerEmail");
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login", "Worker");
            }

            // Get worker ID
            var workerSnapshot = await _firestoreDb.Collection("workers")
                .WhereEqualTo("Email", email)
                .Limit(1)
                .GetSnapshotAsync();

            if (!workerSnapshot.Any())
            {
                return View(new List<BookingModel>());
            }

            var workerId = workerSnapshot.First().Id;

            // Get COMPLETED assignments for this worker
            var assignmentsSnapshot = await _firestoreDb.Collection("assignments")
                .WhereEqualTo("AssignedWorker", workerId)
                .GetSnapshotAsync();

            var completedBookings = new List<BookingModel>();

            foreach (var assignmentDoc in assignmentsSnapshot.Documents)
            {
                var assignmentData = assignmentDoc.ToDictionary();

                // Only include assignments that are marked as fully completed
                if (assignmentData.ContainsKey("IsFullyCompleted") &&
                    assignmentData["IsFullyCompleted"] is bool isCompleted && isCompleted)
                {
                    if (assignmentData.ContainsKey("BookingId"))
                    {
                        var bookingId = assignmentData["BookingId"].ToString();
                        var bookingDoc = await _firestoreDb.Collection("bookings").Document(bookingId).GetSnapshotAsync();

                        if (bookingDoc.Exists)
                        {
                            var bookingData = bookingDoc.ToDictionary();

                            var booking = new BookingModel
                            {
                                BookingId = bookingId,
                                AssignmentId = assignmentDoc.Id,
                                Address = bookingData.ContainsKey("BookingAddress") ?
                                    bookingData["BookingAddress"].ToString() : "N/A",
                                BookingDate = bookingData.ContainsKey("BookingDate") &&
                                    bookingData["BookingDate"] is Timestamp timestamp ?
                                    timestamp.ToDateTime() : DateTime.MinValue,
                                TimeSlot = bookingData.ContainsKey("PreferredTime") ?
                                    bookingData["PreferredTime"].ToString() : "Not specified",
                                ServiceType = bookingData.ContainsKey("ServiceType") ?
                                    bookingData["ServiceType"].ToString() : "General",
                                CustomerName = bookingData.ContainsKey("CustomerName") ?
                                    bookingData["CustomerName"].ToString() : "Customer",
                                WorkerStatus = "Completed",
                                CompletedAt = assignmentData.ContainsKey("CompletedAt") &&
                                    assignmentData["CompletedAt"] is Timestamp completedTimestamp ?
                                    completedTimestamp.ToDateTime() : DateTime.MinValue
                            };

                            completedBookings.Add(booking);
                        }
                    }
                }
            }

            ViewBag.WorkerEmail = email;
            return View(completedBookings.OrderByDescending(b => b.CompletedAt).ToList());
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Remove("WorkerId");
            HttpContext.Session.Remove("WorkerEmail");
            return RedirectToAction("Login");
        }

    }
}
