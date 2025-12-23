using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TheRockWasteManagement.Models;

namespace TheRockWasteManagement.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            var uid = HttpContext.Session.GetString("uid");
            var role = HttpContext.Session.GetString("role");

            if (string.IsNullOrEmpty(uid))
            {
                // Not logged in — show public landing page with login/register options
                ViewBag.IsLoggedIn = false;
                return View();
            }

            // Redirect based on user role
            switch (role)
            {
                case "admin":
                    return RedirectToAction("Dashboard", "Admin");

                case "customer":
                    ViewBag.IsLoggedIn = true;
                    ViewBag.Role = "customer";
                    return View(); // Customer view includes booking & history

                case "worker":
                    return RedirectToAction("Dashboard", "Worker");

                default:
                    // Unrecognized role or corrupted session
                    HttpContext.Session.Clear();
                    return RedirectToAction("Index");
            }
        }

        public IActionResult Privacy()
        {
            return View(); // Static privacy page
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        public IActionResult SendEnquiry(string Name, string Email, string Message)
        {
            // Future enhancement: send real email via SMTP
            TempData["Success"] = "Thank you for contacting us! We will get back to you shortly.";
            return RedirectToAction("Index");
        }
    }
}

