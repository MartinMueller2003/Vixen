﻿using System;

namespace Vixen.Module {
	interface IModuleManagement {
		object Get(Guid id);
		object[] GetAll();
		object Clone(object instance);
	}

	interface IModuleManagement<T> : IModuleManagement
		where T : class, IModuleInstance {
		new T Get(Guid id);
		new T[] GetAll();
		T Clone(T instance);
	}
}
