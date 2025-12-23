namespace TheRockWasteManagement.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalCustomers { get; set; }
        public int TotalAdmins { get; set; } // Optional
        public int TotalWorkers { get; set; } // If you store workers separately
        public int TotalBookings { get; set; }
        public decimal TotalPayments { get; set; }

        // New properties for charts and activity
        public List<MonthlyData> MonthlyPerformance { get; set; }
        public BookingStatusData BookingStatus { get; set; }
        public List<RecentActivity> RecentActivities { get; set; }

        public AdminDashboardViewModel()
        {
            // Initialize collections to avoid null references
            MonthlyPerformance = new List<MonthlyData>();
            BookingStatus = new BookingStatusData();
            RecentActivities = new List<RecentActivity>();
        }
    }

    public class MonthlyData
    {
        public string Month { get; set; }
        public int Bookings { get; set; }
        public int Payments { get; set; }
    }

    public class BookingStatusData
    {
        public int Completed { get; set; }
        public int Pending { get; set; }
        public int Cancelled { get; set; }
        public int InProgress { get; set; }
        public int ReadingBPS { get; set; }
        public int Approved { get; set; }
        public int Assigned { get; set; }
        public int Rejected { get; set; }
    }

    public class RecentActivity
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public string TimeAgo { get; set; }
    }
    public class AdminManagementViewModel
    {
        public List<AdminUser> AdminUsers { get; set; } = new List<AdminUser>();
        public string NewAdminEmail { get; set; }
        public string NewAdminName { get; set; }
        public string NewAdminPhone { get; set; }
        public string NewAdminPassword { get; set; }
    }

    public class AdminUser
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
    }
}