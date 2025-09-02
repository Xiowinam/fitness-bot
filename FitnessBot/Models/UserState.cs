using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitnessBot.Models
{
    public class UserState
    {
        public ConversationState CurrentState { get; set; } = ConversationState.Start;
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public double? Weight { get; set; }
        public int? Height { get; set; }
        public string? Goal { get; set; }
        public string? ActivityLevel { get; set; }
        public bool IsUpdatingWeight { get; set; } = false;

        public void Reset()
        {
            CurrentState = ConversationState.Start;
            Gender = null;
            Age = null;
            Weight = null;
            Height = null;
            Goal = null;
            ActivityLevel = null;
            IsUpdatingWeight = false;
        }

        public void LoadFromProfile(UserParameters parameters)
        {
            Gender = parameters.Gender;
            Age = parameters.Age;
            Weight = parameters.Weight;
            Height = parameters.Height;
            Goal = parameters.Goal;
            ActivityLevel = parameters.ActivityLevel;
        }
    }
}

