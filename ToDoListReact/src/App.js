import React, { useEffect, useState } from 'react';
import service, { setAccessToken } from './service.js';

function App() {
  const [newTodo, setNewTodo] = useState("");
  const [todos, setTodos] = useState([]);
  
  // ניהול המצבים: 'login', 'register', 'tasks'
  const [view, setView] = useState('login');
  const [isLoading, setIsLoading] = useState(true);
  
  // State לטופס התחברות/הרשמה
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");

  // בדיקה ב-mount - תמיד התחל מלוגין
  useEffect(() => {
    setIsLoading(false);
  }, []);

  async function getTodos() {
    try {
      const todos = await service.getTasks();
      setTodos(todos);
    } catch (error) {
      // ה-Interceptor כבר יטפל ב-401, אבל כאן נוכל לוודא שה-View מתעדכן
      if (error.response?.status === 401) setView('login');
    }
  }

  useEffect(() => {
    if (view === 'tasks' && !isLoading) {
      getTodos();
    }
  }, [view, isLoading]);

  // פונקציות לוגין והרשמה
  async function handleLogin(e) {
    e.preventDefault();
    try {
      console.log('Logging in with:', { username, password });
      const response = await service.login(username, password);
      console.log('Login response:', response);
      setAccessToken(response.token);
      setView('tasks');
      setUsername(""); 
      setPassword("");
    } catch (err) {
      console.error('Login error:', err.response?.data || err.message);
      alert("Login failed: " + (err.response?.data || "Server error"));
    }
  }

// קוד מתוקן
async function handleRegister(e) {
    e.preventDefault();
    try {
        console.log('Registering with:', { username, password });
        // 1. נסה לבצע הרשמה
        const registerResponse = await service.register(username, password);
        console.log('Register response:', registerResponse);
        
        // 2. אם הרשמה הצליחה, שמור את הטוקן ישירות מהתגובה
        setAccessToken(registerResponse.token);
        setView('tasks');
        setUsername(""); 
        setPassword("");
    } catch (err) {
        console.error('Registration error:', err.response?.data || err.message);
        alert(`Registration failed: ${err.response?.data || "Server error"}`);
    }
}
  
  function handleLogout() {
      setAccessToken(null);
      localStorage.removeItem('access_token');
      setView('login');
      setUsername("");
      setPassword("");
  }

  // --- פונקציות משימות (ללא שינוי מהותי) ---
  async function createTodo(e) {
    e.preventDefault();
    await service.addTask(newTodo);
    setNewTodo("");
    await getTodos();
  }

  async function updateCompleted(todo, isComplete) {
    await service.setCompleted(todo, isComplete);
    await getTodos();
  }

  async function deleteTodo(id) {
    await service.deleteTask(id);
    await getTodos();
  }

  // --- תצוגה ---
  if (isLoading) {
    return <div className="todoapp" style={{ padding: "20px" }}>טוען...</div>;
  }

  if (view === 'login' || view === 'register') {
    return (
      <div className="todoapp" style={{ padding: "20px" }}>
        <h1>{view === 'login' ? 'Login' : 'Register'}</h1>
        <form onSubmit={view === 'login' ? handleLogin : handleRegister}>
          <input 
            className="new-todo" 
            placeholder="Username" 
            value={username} 
            onChange={e => setUsername(e.target.value)} 
            required 
            style={{ marginBottom: "10px" }}
          />
          <input 
            className="new-todo" 
            type="password"
            placeholder="Password" 
            value={password} 
            onChange={e => setPassword(e.target.value)} 
            required 
          />
          <button type="submit" style={{ marginTop: "10px", padding: "10px", width: "100%" }}>
            {view === 'login' ? 'Sign In' : 'Sign Up'}
          </button>
        </form>
        <p style={{textAlign: 'center', marginTop: '10px', cursor: 'pointer', color: 'blue'}}
           onClick={() => setView(view === 'login' ? 'register' : 'login')}>
           {view === 'login' ? 'No account? Register here' : 'Already have an account? Login'}
        </p>
      </div>
    );
  }

  return (
    <section className="todoapp">
      <header className="header">
        <h1>todos</h1>
        <button onClick={handleLogout} style={{float: "right", margin: "10px"}}>Logout</button>
        <form onSubmit={createTodo}>
          <input className="new-todo" placeholder="What needs to be done?" value={newTodo} onChange={(e) => setNewTodo(e.target.value)} />
        </form>
      </header>
      <section className="main" style={{ display: "block" }}>
        <ul className="todo-list">
          {todos.map(todo => (
            <li className={todo.isComplete ? "completed" : ""} key={todo.id}>
              <div className="view">
                <input className="toggle" type="checkbox" checked={todo.isComplete} onChange={(e) => updateCompleted(todo, e.target.checked)} />
                <label>{todo.name}</label>
                <button className="destroy" onClick={() => deleteTodo(todo.id)}></button>
              </div>
            </li>
          ))}
        </ul>
      </section>
    </section >
  );
}

export default App;