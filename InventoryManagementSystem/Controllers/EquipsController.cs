﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    public class EquipsController : Controller
    {
        public IActionResult equipQryUser()
        {
            return View();
        }
        
        public IActionResult equipQryAdmin()
        {
            return View();
        }

        public IActionResult equipAddAdmin() {

            return View();
        
        }
    }
}
