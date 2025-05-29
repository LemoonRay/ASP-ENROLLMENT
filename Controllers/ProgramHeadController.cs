using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Mvc;
using Npgsql;
using Fresh_University_Enrollment.Models;

namespace Fresh_University_Enrollment.Controllers
{
    public class ProgramHeadController : Controller
    {
        private string connStr = ConfigurationManager.ConnectionStrings["Enrollment"].ConnectionString;

        // Dashboard action
        public ActionResult Dashboard()
        {
            var programs = new List<Program>();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT prog_code, prog_title FROM program ORDER BY prog_title";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        programs.Add(new Program
                        {
                            ProgCode = reader["prog_code"].ToString(),
                            ProgTitle = reader["prog_title"].ToString()
                        });
                    }
                }
            }

            return View("~/Views/Program-Head/Dashboard.cshtml", programs);
        }

        // SetSchedules GET action
        public ActionResult SetSchedules()
        {
            ReloadDropdowns();
            return View("~/Views/Program-Head/SetSchedules.cshtml");
        }

        // Reload all dropdown data into ViewBag
        private void ReloadDropdowns()
        {
            ViewBag.Courses = GetCourses();
            ViewBag.Rooms = GetRooms();
            ViewBag.Instructors = GetInstructors();
            ViewBag.AcademicYears = GetAcademicYears();
            ViewBag.Sections = GetSections();
        }

        // POST Add Schedule with conflict check
        [HttpPost]
        public ActionResult AddSchedule(Schedule sched)
        {
            if (!ModelState.IsValid)
            {
                ReloadDropdowns();
                return View("~/Views/Program-Head/SetSchedules.cshtml", sched);
            }

            if (sched.TimeEnd <= sched.TimeStart)
            {
                ModelState.AddModelError("TimeEnd", "End Time must be later than Start Time.");
                ReloadDropdowns();
                return View("~/Views/Program-Head/SetSchedules.cshtml", sched);
            }

            bool conflictExists = false;
            string conflictMessage = "";

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                // Check room conflict
                string roomConflictSql = @"
                    SELECT COUNT(*) FROM schedule
                    WHERE ay_code = @ayCode AND day = @day AND room = @room
                    AND NOT (time_end <= @start OR time_start >= @end)";

                using (var cmd = new NpgsqlCommand(roomConflictSql, conn))
                {
                    cmd.Parameters.AddWithValue("@ayCode", sched.AyCode);
                    cmd.Parameters.AddWithValue("@day", sched.Day);
                    cmd.Parameters.AddWithValue("@room", sched.Room);
                    cmd.Parameters.AddWithValue("@start", sched.TimeStart);
                    cmd.Parameters.AddWithValue("@end", sched.TimeEnd);
                    var roomConflictCount = (long)cmd.ExecuteScalar();

                    if (roomConflictCount > 0)
                    {
                        conflictExists = true;
                        conflictMessage = $"Scheduling conflict: The room {sched.Room} is already booked on {sched.Day} during this time.";
                    }
                }

                // Check instructor conflict if no room conflict
                if (!conflictExists)
                {
                    string instructorConflictSql = @"
                        SELECT COUNT(*) FROM schedule
                        WHERE ay_code = @ayCode AND day = @day AND instructor_name = @instructor
                        AND NOT (time_end <= @start OR time_start >= @end)";

                    using (var cmd = new NpgsqlCommand(instructorConflictSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@ayCode", sched.AyCode);
                        cmd.Parameters.AddWithValue("@day", sched.Day);
                        cmd.Parameters.AddWithValue("@instructor", sched.InstructorName);
                        cmd.Parameters.AddWithValue("@start", sched.TimeStart);
                        cmd.Parameters.AddWithValue("@end", sched.TimeEnd);
                        var instructorConflictCount = (long)cmd.ExecuteScalar();

                        if (instructorConflictCount > 0)
                        {
                            conflictExists = true;
                            conflictMessage = $"Scheduling conflict: Instructor {sched.InstructorName} is already booked on {sched.Day} during this time.";
                        }
                    }
                }

                if (conflictExists)
                {
                    ModelState.AddModelError("", conflictMessage);
                    ReloadDropdowns();
                    return View("~/Views/Program-Head/SetSchedules.cshtml", sched);
                }

                // Insert schedule if no conflict
                string insertSql = @"
                    INSERT INTO schedule (crs_code, section, instructor_name, room, day, time_start, time_end, ay_code, semester)
                    VALUES (@code, @section, @instructor, @room, @day, @start, @end, @ay, @sem)";
                using (var cmd = new NpgsqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@code", sched.CrsCode);
                    cmd.Parameters.AddWithValue("@section", sched.Section);
                    cmd.Parameters.AddWithValue("@instructor", sched.InstructorName);
                    cmd.Parameters.AddWithValue("@room", sched.Room);
                    cmd.Parameters.AddWithValue("@day", sched.Day);
                    cmd.Parameters.AddWithValue("@start", sched.TimeStart);
                    cmd.Parameters.AddWithValue("@end", sched.TimeEnd);
                    cmd.Parameters.AddWithValue("@ay", sched.AyCode);
                    cmd.Parameters.AddWithValue("@sem", sched.Semester);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("SetSchedules");
        }

        // JSON: Get sections for a selected course
        public JsonResult GetSectionsByCourse(string courseCode)
        {
            var sections = new List<Section>();
            if (string.IsNullOrWhiteSpace(courseCode))
                return Json(sections, JsonRequestBehavior.AllowGet);

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT sectionid, sectionname FROM section WHERE coursecode = @courseCode ORDER BY sectionname";
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@courseCode", courseCode);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sections.Add(new Section
                            {
                                SectionId = (int)reader["sectionid"],
                                SectionName = reader["sectionname"].ToString()
                            });
                        }
                    }
                }
            }
            return Json(sections, JsonRequestBehavior.AllowGet);
        }

        // AJAX: Add section linked to a course, prevent duplicates
        [HttpPost]
        public JsonResult AddSection(string sectionName, string courseCode)
        {
            if (string.IsNullOrWhiteSpace(sectionName) || string.IsNullOrWhiteSpace(courseCode))
                return Json(new { success = false, message = "Section name and course must be specified." });

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                string checkSql = "SELECT COUNT(*) FROM section WHERE sectionname = @sectionName AND coursecode = @courseCode";
                using (var checkCmd = new NpgsqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@sectionName", sectionName);
                    checkCmd.Parameters.AddWithValue("@courseCode", courseCode);
                    var exists = (long)checkCmd.ExecuteScalar() > 0;
                    if (exists)
                        return Json(new { success = false, message = "Section already exists for this course." });
                }

                string insertSql = "INSERT INTO section (sectionname, coursecode) VALUES (@sectionName, @courseCode)";
                using (var insertCmd = new NpgsqlCommand(insertSql, conn))
                {
                    insertCmd.Parameters.AddWithValue("@sectionName", sectionName);
                    insertCmd.Parameters.AddWithValue("@courseCode", courseCode);
                    insertCmd.ExecuteNonQuery();
                }
            }
            return Json(new { success = true });
        }

        // Helper: get all courses
        private List<Course> GetCourses()
        {
            var courses = new List<Course>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT crs_code, crs_title FROM course ORDER BY crs_title";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        courses.Add(new Course
                        {
                            Crs_Code = reader["crs_code"].ToString(),
                            Crs_Title = reader["crs_title"].ToString()
                        });
                    }
                }
            }
            return courses;
        }

        // Helper: get rooms
        private List<Room> GetRooms()
        {
            var rooms = new List<Room>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT room_id, room_name FROM room ORDER BY room_name";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rooms.Add(new Room
                        {
                            RoomId = (int)reader["room_id"],
                            RoomName = reader["room_name"].ToString()
                        });
                    }
                }
            }
            return rooms;
        }

        // Helper: get sections
        private List<Section> GetSections()
        {
            var sections = new List<Section>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT sectionid, sectionname FROM section ORDER BY sectionname";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sections.Add(new Section
                        {
                            SectionId = (int)reader["sectionid"],
                            SectionName = reader["sectionname"].ToString()
                        });
                    }
                }
            }
            return sections;
        }

        // Helper: get academic years
        private List<AcademicYear> GetAcademicYears()
        {
            var list = new List<AcademicYear>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT ay_code, ay_start_year, ay_end_year FROM academic_year ORDER BY ay_start_year";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new AcademicYear
                        {
                            AyCode = reader["ay_code"].ToString(),
                            AyStartYear = (int)reader["ay_start_year"],
                            AyEndYear = (int)reader["ay_end_year"]
                        });
                    }
                }
            }
            return list;
        }

        // Helper: get instructors
        private List<Instructor> GetInstructors()
        {
            var instructors = new List<Instructor>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT instructor_id, instructor_name FROM instructor ORDER BY instructor_name";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        instructors.Add(new Instructor
                        {
                            InstructorId = (int)reader["instructor_id"],
                            InstructorName = reader["instructor_name"].ToString()
                        });
                    }
                }
            }
            return instructors;
        }

        // Get courses filtered by semester for AJAX call
        public JsonResult GetCoursesBySemester(string semester)
        {
            var courses = new List<Course>();
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT DISTINCT c.crs_code, c.crs_title 
                    FROM course c
                    INNER JOIN curriculum_course cc ON c.crs_code = cc.crs_code
                    WHERE cc.cur_semester = @semester
                    ORDER BY c.crs_title";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@semester", semester);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            courses.Add(new Course
                            {
                                Crs_Code = reader["crs_code"].ToString(),
                                Crs_Title = reader["crs_title"].ToString()
                            });
                        }
                    }
                }
            }
            return Json(courses, JsonRequestBehavior.AllowGet);
        }

        // Get filtered schedules for AJAX view table
        public JsonResult GetSchedules(string ayCode, string semester)
        {
            var schedules = new List<object>();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                string sql = @"
            SELECT crs_code, section, instructor_name, room, day, time_start, time_end, ay_code, semester
            FROM schedule
            WHERE (@ayCode = '' OR ay_code = @ayCode)
              AND (@semester = '' OR semester = @semester)
            ORDER BY ay_code, semester, day, time_start";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ayCode", string.IsNullOrEmpty(ayCode) ? "" : ayCode);
                    cmd.Parameters.AddWithValue("@semester", string.IsNullOrEmpty(semester) ? "" : semester);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            schedules.Add(new
                            {
                                CrsCode = reader["crs_code"].ToString(),
                                Section = reader["section"].ToString(),
                                InstructorName = reader["instructor_name"].ToString(),
                                Room = reader["room"].ToString(),
                                Day = reader["day"].ToString(),
                                TimeStart = ((TimeSpan)reader["time_start"]).ToString(@"hh\:mm"),
                                TimeEnd = ((TimeSpan)reader["time_end"]).ToString(@"hh\:mm"),
                                AyCode = reader["ay_code"].ToString(),
                                Semester = reader["semester"].ToString()
                            });
                        }
                    }
                }
            }
            return Json(schedules, JsonRequestBehavior.AllowGet);
        }


        public ActionResult Students()
        {
            return View("~/Views/Program-Head/ViewStudentList.cshtml");
        }

        public ActionResult Approval()
        {
            return View("~/Views/Program-Head/EnrollmentApproval.cshtml");
        }

        public ActionResult Schedule()
        {
            return RedirectToAction("SetSchedules");
        }

        public ActionResult StudentManagement()
        {
            return View("~/Views/Program-Head/StudentManagement.cshtml");
        }

        public ActionResult ClassManagement()
        {
            return View("~/Views/Program-Head/ClassManagement.cshtml");
        }
    }
}
