using System;
using System.ComponentModel.DataAnnotations;

namespace Fresh_University_Enrollment.Models;
public class Schedule
{
    public int SchedId { get; set; }
    public string CrsCode { get; set; }
    public string Section { get; set; }
    public string InstructorName { get; set; }
    public string Room { get; set; }
    public string Day { get; set; }
    public TimeSpan TimeStart { get; set; }
    public TimeSpan TimeEnd { get; set; }
    public string AyCode { get; set; }
    public string Semester { get; set; }
}
