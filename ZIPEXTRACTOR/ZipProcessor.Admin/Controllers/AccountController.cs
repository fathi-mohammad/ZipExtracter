using Microsoft.AspNetCore.Mvc;

public class AccountController : Controller
{
    private const string AdminUser = "admin";
    private const string AdminPass = "admin";

    public IActionResult Login() => View();

    [HttpPost]
    public IActionResult Loginold(string username, string password)
    {
        if (username == AdminUser && password == AdminPass)
        {
            HttpContext.Session.SetString("User", username);
            return RedirectToAction("Index", "Dashboard");
        }

        ViewBag.Error = "Invalid username or password";
        return View();
    }



    [HttpPost]
    public IActionResult Login(string username, string password, string mode)
    {
       
        if (username == AdminUser && password == AdminPass)
        {
           
            if (mode == "docker")
            {
                HttpContext.Session.SetString("User", username);
           
                TempData["Message"] = "Logged in with Docker mode.";
                return RedirectToAction("IndexDocker", "Dashboard");
            }
            else if (mode == "executable")
            {
                HttpContext.Session.SetString("User", username);
              
                TempData["Message"] = "Logged in with Executable mode.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Error = "Select Mode.";
            return View();
        }

        ViewBag.Error = "Invalid credentials.";
        return View();
    }


    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}
