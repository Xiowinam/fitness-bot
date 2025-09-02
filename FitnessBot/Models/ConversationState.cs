using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitnessBot.Models
{
    public enum ConversationState
    {
        Start,
        AskingGender,
        AskingAge,
        AskingWeight,
        AskingHeight,
        AskingGoal,
        AskingActivity,
        ProcessingResults,
        MainMenu,
        UpdatingWeight,
        ShowingProfile,
        ShowingExercisesMenu,
        ShowingExercisesList
    }
}
