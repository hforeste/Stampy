namespace StampyWorker.Jobs
{
    internal class ActionArgument
    {
        public string ParameterName { get; set; }
        public string Switch { get; set; }
        public string Value { get; set; }
        public string MiddleExpression { get; set; }

        private string _actionArgumentToStringResponse;

        public ActionArgument(string parameterName, string middleExpression, string value)
        {
            ParameterName = parameterName;
            MiddleExpression = middleExpression;
            Value = value;
            _actionArgumentToStringResponse = $"{ParameterName}{middleExpression}{Value}";
        }

        public ActionArgument(string parameter)
        {
            Value = parameter;
            _actionArgumentToStringResponse = parameter;
        }

        public ActionArgument(string switchSymbol, string value)
        {
            Switch = switchSymbol;
            Value = value;
            _actionArgumentToStringResponse = $"{Switch} {Value}";
        }

        public override string ToString()
        {
            return _actionArgumentToStringResponse;
        }
    }
}
