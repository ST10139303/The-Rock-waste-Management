namespace TheRockWasteManagement.Models
{
    public class WorkerModel
    {
        public string Id { get; set; } = string.Empty; // Firestore Document ID
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; } = true;

        // Additional properties for better functionality
        public string Specialization { get; set; }
        public int CompletedJobs { get; set; }
        public double Rating { get; set; }
        public DateTime JoinDate { get; set; }
        public string Status { get; set; } = "Available"; // Available, Busy, Offline
    }
}