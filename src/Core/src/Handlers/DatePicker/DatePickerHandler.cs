﻿namespace Microsoft.Maui.Handlers
{
	public partial class DatePickerHandler
	{
		public static PropertyMapper<IDatePicker, DatePickerHandler> DatePickerMapper = new PropertyMapper<IDatePicker, DatePickerHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IDatePicker.Format)] = MapFormat,
			[nameof(IDatePicker.Date)] = MapDate,
			[nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
			[nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
			[nameof(IDatePicker.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IDatePicker.Font)] = MapFont
		};

		public DatePickerHandler() : base(DatePickerMapper)
		{

		}

		public DatePickerHandler(PropertyMapper mapper) : base(mapper)
		{

		}
	}
}