namespace RunCat365
{
    enum Runner
    {
        Cat,
        Parrot,
        Horse,
        Puppy,
        Dino,
        Rabbit,
    }

    static class RunnerExtensions
    {
        internal static string GetString(this Runner runner)
        {
            switch (runner)
            {
                case Runner.Cat:
                    return "Cat";
                case Runner.Parrot:
                    return "Parrot";
                case Runner.Horse:
                    return "Horse";
                case Runner.Puppy:
                    return "Puppy";
                case Runner.Dino:
                    return "Dino";
                case Runner.Rabbit:
                    return "Rabbit";
                default:
                    return "";
            }
        }

        internal static int GetFrameNumber(this Runner runner)
        {
            switch (runner)
            {
                case Runner.Cat:
                    return 5;
                case Runner.Parrot:
                    return 10;
                case Runner.Horse:
                    return 14;
                case Runner.Puppy:
                    return 5;
                case Runner.Dino:
                    return 7;
                case Runner.Rabbit:
                    return 5;
                default:
                    return 0;
            }
        }
    }
}
