using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitnessBot.Models
{
    public class UserParameters
    {
        public double Weight { get; set; }
        public int Height { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;
        public string ActivityLevel { get; set; } = string.Empty;
        public int DailyCalories { get; set; }
        public int ProteinGoal { get; set; }
        public int FatGoal { get; set; }
        public int CarbsGoal { get; set; }
        public string WorkoutPlan { get; set; } = string.Empty;
        public string DietAdvice { get; set; } = string.Empty;
    }
}
