using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using TheRockWasteManagement.Models;
using System.Net.Mail;
using System.Net;

namespace TheRockWasteManagement.Controllers
{
    public class CustomerController : Controller
    {
        private readonly FirestoreDb _firestoreDb;

        public CustomerController()
        {
            string projectId = "therockwastemanagement";
            _firestoreDb = FirestoreDb.Create(projectId);
        }

        [HttpGet("Customer/Dashboard")]
        public IActionResult Dashboard()
        {
            // Allow access even if email is not verified
            if (!IsCustomerAuthenticated())
            {
                return RedirectToAction("Login", "Auth");
            }

            var uid = HttpContext.Session.GetString("uid");
            var email = HttpContext.Session.GetString("email");
            var emailVerified = HttpContext.Session.GetString("EmailVerified") == "True";

            ViewBag.UserId = uid;
            ViewBag.UserEmail = email;
            ViewBag.EmailVerified = emailVerified;

            return View();
        }

        [HttpPost("Customer/Logout")]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Auth");
        }

        private bool IsCustomerAuthenticated()
        {
            return HttpContext.Session.GetString("IsLoggedIn") == "true" &&
                   HttpContext.Session.GetString("role") == "customer" &&
                   !string.IsNullOrEmpty(HttpContext.Session.GetString("uid"));
        }


        public IActionResult BookCleaning()
        {
            var role = HttpContext.Session.GetString("role");
            if (role != "customer") return Redirect("/Auth/Login");

            // Get customer name from session or database
            var customerName = HttpContext.Session.GetString("customerName");
            if (string.IsNullOrEmpty(customerName))
            {
                // If not in session, get from database
                customerName = GetCustomerNameFromDatabase().Result ?? "Customer";
            }

            ViewBag.CustomerName = customerName;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> BookCleaning(DateTime bookingDate, string bookingTime, string address,
            string serviceType, decimal estimatedPrice, string binSize = "", string carpetSize = "",
            string specialRequest = "")
        {
            var uid = HttpContext.Session.GetString("uid") ?? "unknown";
            var customerName = HttpContext.Session.GetString("customerName") ?? "Customer";

            // ✅ CHECK IF CUSTOMER ALREADY HAS APPROVED/PENDING BOOKING FOR THIS DATE
            var existingBookings = await _firestoreDb.Collection("bookings")
                .WhereEqualTo("CustomerId", uid)
                .WhereEqualTo("BookingDate", Timestamp.FromDateTime(bookingDate.ToUniversalTime()))
                .GetSnapshotAsync();

            bool hasActiveBooking = false;
            foreach (var doc in existingBookings.Documents)
            {
                var bookingData = doc.ToDictionary();
                if (bookingData.ContainsKey("Status"))
                {
                    var status = bookingData["Status"].ToString().ToLower();
                    // Don't allow new booking if there's an approved or pending booking for same date
                    if (status == "approved" || status == "pending" || status == "assigned")
                    {
                        hasActiveBooking = true;
                        break;
                    }
                }
            }

            if (hasActiveBooking)
            {
                ModelState.AddModelError(string.Empty, "You already have an active booking for this date. Please cancel your existing booking or choose a different date.");
                ViewBag.CustomerName = customerName;
                return View();
            }

            var booking = new Dictionary<string, object>
    {
        { "CustomerId", uid },
        { "CustomerName", customerName }, // ✅ Now using actual customer name from session
        { "BookingAddress", address },
        { "BookingDate", Timestamp.FromDateTime(bookingDate.ToUniversalTime()) },
        { "PreferredTime", bookingTime },
        { "Status", "pending" },
        { "ServiceType", serviceType },
        { "EstimatedPrice", (double)estimatedPrice },
        { "FinalPrice", 0.0 },
        { "IsPriceSet", false },
        { "PaymentStatus", "pending" },
        { "CreatedAt", Timestamp.FromDateTime(DateTime.UtcNow) },
        { "UpdatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
    };

            // Add size information if provided
            if (!string.IsNullOrEmpty(binSize))
            {
                booking.Add("BinSize", binSize);
            }

            if (!string.IsNullOrEmpty(carpetSize))
            {
                booking.Add("CarpetSize", carpetSize);
            }

            if (!string.IsNullOrEmpty(specialRequest))
            {
                booking.Add("SpecialRequest", specialRequest);
            }

            await _firestoreDb.Collection("bookings").AddAsync(booking);
            await SendConfirmationEmail(customerName, bookingDate, bookingTime, address, serviceType);

            ViewBag.Success = true;
            ViewBag.BookingDate = bookingDate.ToShortDateString();
            ViewBag.Address = address;
            ViewBag.ServiceType = serviceType;
            ViewBag.CustomerName = customerName;

            return View();
        }

        // Helper method to get customer name from database
        private async Task<string> GetCustomerNameFromDatabase()
        {
            var uid = HttpContext.Session.GetString("uid");
            if (string.IsNullOrEmpty(uid)) return null;

            try
            {
                var userDoc = await _firestoreDb.Collection("users").Document(uid).GetSnapshotAsync();
                if (userDoc.Exists)
                {
                    var userData = userDoc.ToDictionary();
                    if (userData.ContainsKey("name"))
                    {
                        var name = userData["name"].ToString();
                        // Store in session for future use
                        HttpContext.Session.SetString("customerName", name);
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting customer name: {ex.Message}");
            }

            return null;
        }

        private async Task SendConfirmationEmail(string customerName, DateTime date, string time, string address, string serviceType)
        {
            var subject = "Cleaning Service Booking Confirmation";
            var body = $@"
        <h2>Booking Confirmation</h2>
        <p>Dear {customerName},</p>
        <p>Your {serviceType.Replace("_", " ")} cleaning has been booked successfully!</p>
        <p><strong>Date:</strong> {date.ToShortDateString()}</p>
        <p><strong>Time:</strong> {time}</p>
        <p><strong>Address:</strong> {address}</p>
        <p>The admin will review your booking and set the final price shortly.</p>
        <p>Thank you for choosing The Rock Waste Management!</p>
    ";
            // Implement your email sending logic here
        }

        private async Task SendConfirmationEmail(string email, DateTime date, string time, string address)
        {
            var subject = "Cleaning Service Booking Confirmation";
            var body = $"Your cleaning has been booked for {date.ToShortDateString()} ({time}) at {address}.";
        }

        public async Task<IActionResult> BookingHistory()
        {
            var uid = HttpContext.Session.GetString("uid");
            var role = HttpContext.Session.GetString("role");
            if (string.IsNullOrEmpty(uid) || role != "customer")
                return Redirect("/Auth/Login");

            var snapshot = await _firestoreDb.Collection("bookings")
                .WhereEqualTo("CustomerId", uid)
                .GetSnapshotAsync();

            var bookingList = new List<BookingViewModel>();

            foreach (var doc in snapshot.Documents)
            {
                var data = doc.ToDictionary();

                DateTime bookingDate = DateTime.Now;
                if (data.ContainsKey("BookingDate") && data["BookingDate"] is Timestamp ts)
                {
                    bookingDate = ts.ToDateTime();
                }

                // Handle price conversion
                double finalPrice = 0;
                if (data.ContainsKey("FinalPrice"))
                {
                    if (data["FinalPrice"] is double priceDouble)
                        finalPrice = priceDouble;
                    else if (data["FinalPrice"] is int priceInt)
                        finalPrice = priceInt;
                }

                bookingList.Add(new BookingViewModel
                {
                    BookingId = doc.Id,
                    Date = bookingDate,
                    Address = data.ContainsKey("BookingAddress") ? data["BookingAddress"].ToString() : "",
                    Status = data.ContainsKey("Status") ? data["Status"].ToString() : "Unknown",
                    FinalPrice = finalPrice,
                    ServiceType = data.ContainsKey("ServiceType") ? data["ServiceType"].ToString() : "Unknown",
                    PaymentStatus = data.ContainsKey("PaymentStatus") ? data["PaymentStatus"].ToString() : "pending"
                });
            }

            return View(bookingList);
        }

        [HttpPost]
        public async Task<IActionResult> CancelBooking(string bookingId)
        {
            var uid = HttpContext.Session.GetString("uid");
            var role = HttpContext.Session.GetString("role");

            if (string.IsNullOrEmpty(uid) || role != "customer")
                return Redirect("/Auth/Login");

            if (string.IsNullOrEmpty(bookingId))
                return BadRequest("Invalid booking ID.");

            var bookingRef = _firestoreDb.Collection("bookings").Document(bookingId);
            var snapshot = await bookingRef.GetSnapshotAsync();

            if (!snapshot.Exists)
                return NotFound("Booking not found.");

            var data = snapshot.ToDictionary();
            if (data["CustomerId"].ToString() != uid)
                return Forbid("You are not authorized to cancel this booking.");

            await bookingRef.UpdateAsync(new Dictionary<string, object>
            {
                { "Status", "cancelled" }
            });

            TempData["SuccessMessage"] = "Your booking has been successfully cancelled.";

            if (data.ContainsKey("CustomerEmail"))
            {
                string customerEmail = data["CustomerEmail"].ToString();
                await SendCancellationEmail(customerEmail, data);
            }

            return RedirectToAction("BookingHistory");
        }

        private async Task SendCancellationEmail(string toEmail, Dictionary<string, object> data)
        {
            var message = new MailMessage();
            message.To.Add(toEmail);
            message.Subject = "Booking Cancellation Confirmation";
            message.Body = $"Dear {data["CustomerName"]},\n\nYour booking for {data["BookingAddress"]} on {((Timestamp)data["BookingDate"]).ToDateTime().ToString("dd MMM yyyy")} has been successfully cancelled.\n\nRegards,\nThe Rock Waste Management Team";
            message.IsBodyHtml = false;
            message.From = new MailAddress("your-email@example.com");

            using (var smtp = new SmtpClient("smtp.gmail.com", 587))
            {
                smtp.Credentials = new NetworkCredential("your-email@example.com", "your-email-password");
                smtp.EnableSsl = true;
                await smtp.SendMailAsync(message);
            }
        }
        [HttpGet]
        public async Task<IActionResult> MakePayment()
        {
            var uid = HttpContext.Session.GetString("uid");
            if (string.IsNullOrEmpty(uid))
                return Redirect("/Auth/Login");

            // Get pending bookings with prices set
            var pendingBookingsSnapshot = await _firestoreDb.Collection("bookings")
                .WhereEqualTo("CustomerId", uid)
                .WhereEqualTo("IsPriceSet", true)
                .WhereEqualTo("PaymentStatus", "pending")
                .WhereIn("Status", new[] { "approved", "assigned" })
                .GetSnapshotAsync();

            var pendingBookings = new List<dynamic>();
            foreach (var doc in pendingBookingsSnapshot.Documents)
            {
                var data = doc.ToDictionary();

                double finalPrice = 0;
                if (data.ContainsKey("FinalPrice"))
                {
                    if (data["FinalPrice"] is double priceDouble)
                        finalPrice = priceDouble;
                    else if (data["FinalPrice"] is int priceInt)
                        finalPrice = priceInt;
                }

                pendingBookings.Add(new
                {
                    BookingId = doc.Id,
                    ServiceType = data.ContainsKey("ServiceType") ? data["ServiceType"].ToString() : "Unknown",
                    Address = data.ContainsKey("BookingAddress") ? data["BookingAddress"].ToString() : "",
                    BookingDate = data.ContainsKey("BookingDate") && data["BookingDate"] is Timestamp ts ? ts.ToDateTime() : DateTime.Now,
                    TimeSlot = data.ContainsKey("PreferredTime") ? data["PreferredTime"].ToString() : "",
                    FinalPrice = finalPrice
                });
            }

            ViewBag.PendingBookings = pendingBookings;
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> MakePayment(string bookingId, double amount, string paymentMethod, string reference, string description, string serviceType)
        {
            var uid = HttpContext.Session.GetString("uid");

            // Fetch customer name directly from Firestore using firstName and lastName
            string customerName = "Unknown Customer";
            try
            {
                var userDoc = await _firestoreDb.Collection("users").Document(uid).GetSnapshotAsync();
                if (userDoc.Exists)
                {
                    var userData = userDoc.ToDictionary();

                    string firstName = userData.ContainsKey("firstName") && userData["firstName"] != null
                        ? userData["firstName"].ToString()
                        : "";

                    string lastName = userData.ContainsKey("lastName") && userData["lastName"] != null
                        ? userData["lastName"].ToString()
                        : "";

                    customerName = $"{firstName} {lastName}".Trim();

                    // If still empty, try other fields
                    if (string.IsNullOrEmpty(customerName) && userData.ContainsKey("name") && userData["name"] != null)
                        customerName = userData["name"].ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting customer name: {ex.Message}");
            }

            try
            {
                var paymentData = new Dictionary<string, object>
        {
            { "CustomerId", uid },
            { "CustomerName", customerName },
            { "Amount", amount },
            { "PaymentMethod", paymentMethod },
            { "Reference", reference ?? "" },
            { "Description", description ?? $"Payment for {serviceType} cleaning" },
            { "PaymentDate", Timestamp.FromDateTime(DateTime.UtcNow) },
            { "Status", "completed" }
        };

                // Rest of your payment logic...
                if (!string.IsNullOrEmpty(bookingId))
                {
                    paymentData["BookingId"] = bookingId;
                    await _firestoreDb.Collection("bookings").Document(bookingId).UpdateAsync(new Dictionary<string, object>
            {
                { "PaymentStatus", "paid" },
                { "UpdatedAt", DateTime.UtcNow }
            });
                }

                await _firestoreDb.Collection("payments").AddAsync(paymentData);

                ViewBag.PaymentSuccess = true;
                ViewBag.Amount = amount;

                return await MakePayment();
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Payment failed: {ex.Message}";
                return await MakePayment();
            }
        }
        // Helper method to get customer name from Firestore using firstName and lastName
        private async Task<string> GetCustomerNameFromFirestore(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return "Unknown Customer";

            try
            {
                var userDoc = await _firestoreDb.Collection("users").Document(uid).GetSnapshotAsync();
                if (userDoc.Exists)
                {
                    var userData = userDoc.ToDictionary();

                    // Use firstName and lastName fields from your Firestore structure
                    string firstName = "";
                    string lastName = "";

                    if (userData.ContainsKey("firstName") && userData["firstName"] != null)
                        firstName = userData["firstName"].ToString();

                    if (userData.ContainsKey("lastName") && userData["lastName"] != null)
                        lastName = userData["lastName"].ToString();

                    // Combine first and last name
                    if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
                        return $"{firstName} {lastName}".Trim();
                    else if (!string.IsNullOrEmpty(firstName))
                        return firstName;
                    else if (!string.IsNullOrEmpty(lastName))
                        return lastName;
                    else if (userData.ContainsKey("email") && userData["email"] != null)
                        return userData["email"].ToString().Split('@')[0]; // Use email username as fallback
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching customer name: {ex.Message}");
            }

            return "Customer"; // Fallback
        }
        [HttpGet("CheckActiveBooking")]
        public async Task<IActionResult> CheckActiveBooking(DateTime date)
        {
            var uid = HttpContext.Session.GetString("uid");
            if (string.IsNullOrEmpty(uid))
                return Json(new { hasActiveBooking = false });

            try
            {
                var existingBookings = await _firestoreDb.Collection("bookings")
                    .WhereEqualTo("CustomerId", uid)
                    .WhereEqualTo("BookingDate", Timestamp.FromDateTime(date.ToUniversalTime()))
                    .GetSnapshotAsync();

                bool hasActiveBooking = false;
                foreach (var doc in existingBookings.Documents)
                {
                    var bookingData = doc.ToDictionary();
                    if (bookingData.ContainsKey("Status"))
                    {
                        var status = bookingData["Status"].ToString().ToLower();
                        // Consider approved, pending, and assigned as active bookings
                        if (status == "approved" || status == "pending" || status == "assigned")
                        {
                            hasActiveBooking = true;
                            break;
                        }
                    }
                }

                return Json(new { hasActiveBooking });
            }
            catch (Exception ex)
            {
                return Json(new { hasActiveBooking = false, error = ex.Message });
            }
        }
    }
}


