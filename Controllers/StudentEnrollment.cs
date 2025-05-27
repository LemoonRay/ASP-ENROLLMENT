using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Mvc;
using Fresh_University_Enrollment.Models;
using Npgsql;

namespace Fresh_University_Enrollment.Controllers
{
    
    
    public class StudentEnrollmentController : Controller
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["Enrollment"].ConnectionString;

[HttpGet]
public JsonResult GetAvailableSubjects(string cur_year_level, string cur_semester, string prog_code)

{
    try
    {
        var subjects = new List<SubjectViewModel>();

        using (var conn = new NpgsqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
                SELECT 
                    s.crs_code,
                    c.crs_title,
                    se.tsl_start_time,
                    se.tsl_end_time,
                    se.tsl_day,
                    c.crs_units
                FROM schedule s
                JOIN course c ON s.crs_code = c.crs_code
                JOIN session se ON s.schd_id = se.schd_id
                JOIN curriculum_course cc ON cc.crs_code = s.crs_code
                WHERE cc.cur_year_level = @cur_year_level
                  AND cc.cur_semester = @cur_semester
                  AND cc.prog_code = @prog_code";

            using (var cmd = new NpgsqlCommand(query, conn))
            {
                // cmd.Parameters.AddWithValue("@crs_code", crs_code);
                cmd.Parameters.AddWithValue("@cur_year_level", cur_year_level);
                cmd.Parameters.AddWithValue("@cur_semester", cur_semester);
                cmd.Parameters.AddWithValue("@prog_code", prog_code);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var dayNum = Convert.ToInt32(reader["tsl_day"]);
                        var dayStr = dayNum switch
                        {
                            1 => "M",
                            2 => "T",
                            3 => "W",
                            4 => "Th",
                            5 => "F",
                            _ => "N/A"
                        };

                        var startTime = reader["tsl_start_time"] == DBNull.Value ? "" : reader["tsl_start_time"].ToString();
                        var endTime = reader["tsl_end_time"] == DBNull.Value ? "" : reader["tsl_end_time"].ToString();

                        subjects.Add(new SubjectViewModel
                        {
                            CourseCode = reader["crs_code"]?.ToString(),
                            Title = reader["crs_title"]?.ToString(),
                            Time = $"{startTime} - {endTime}",
                            Days = dayStr,
                            Room = "N/A",
                            Units = Convert.ToInt32(reader["crs_units"])
                        });
                    }
                }
            }
        }

        return Json(subjects, JsonRequestBehavior.AllowGet);
    }
    catch (Exception ex)
    {
        return Json(new { error = ex.Message, stackTrace = ex.StackTrace }, JsonRequestBehavior.AllowGet);
    }
}





        public ActionResult Student_Enrollment()
        {
            var sessionStudCode = Session["Stud_Code"];

            if (sessionStudCode == null)
            {
                return RedirectToAction("Login", "Account");
            }

            int studCode;
            if (!int.TryParse(sessionStudCode.ToString(), out studCode))
            {
                return RedirectToAction("Login", "Account");
            }

            try
            {
                // Load student data
                var student = GetStudentById(studCode);
                if (student == null)
                {
                    ViewBag.ErrorMessage = "Student not found.";
                    return View("~/Views/Shared/Error.cshtml");
                }

                // Load programs for dropdown
                var programs = GetProgramsFromDatabase();
                ViewBag.Programs = programs;
                var academicYears = GetAcademicYears();
                ViewBag.AcademicYears = academicYears;

                // Return the view with student model and programs in ViewBag
                return View("~/Views/Main/StudentEnroll.cshtml", student);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error loading student data: {ex.Message}";
                return View("~/Views/Shared/Error.cshtml");
            }
        }

        private Student GetStudentById(int studCode)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM STUDENT WHERE STUD_CODE = @studCode";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@studCode", studCode);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Student
                            {
                                Stud_Id = reader.GetInt32(reader.GetOrdinal("stud_id")),
                                Stud_Lname = reader["stud_lname"]?.ToString(),
                                Stud_Fname = reader["stud_fname"]?.ToString(),
                                Stud_Mname = reader["stud_mname"]?.ToString(),
                                Stud_Dob = Convert.ToDateTime(reader["stud_dob"]),
                                Stud_Contact = reader["stud_contact"]?.ToString(),
                                Stud_Email = reader["stud_email"]?.ToString(),
                                Stud_Address = reader["stud_address"]?.ToString(),
                                Stud_Code = Convert.ToInt32(reader["stud_code"])
                            };
                        }
                    }
                }
            }

            return null;
        }

        private List<Program> GetProgramsFromDatabase()
        {
            var programs = new List<Program>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand("SELECT \"prog_code\", \"prog_title\" FROM \"program\"", conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            programs.Add(new Program
                            {
                                ProgCode = reader.GetString(0),
                                ProgTitle = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            return programs;
        }
        private List<AcademicYear> GetAcademicYears()
        {
            var academicYears = new List<AcademicYear>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand("SELECT ay_code, ay_start_year, ay_end_year FROM academic_year", conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            academicYears.Add(new AcademicYear
                            {
                                AyCode = reader.GetString(0),
                                AyStartYear = reader.GetInt32(1),
                                AyEndYear = reader.GetInt32(2)
                            });
                        }
                    }
                }
            }

            return academicYears;
        }

    }
}