import axios from 'axios';

const api = axios.create({
  baseURL: process.env.REACT_APP_API_URL,
  timeout: 5000,
});

// פונקציה לעדכון הטוקן ב-Header (תיקרא לאחר לוגין)
export const setAccessToken = (token) => {
  if (token) {
    api.defaults.headers.common['Authorization'] = `Bearer ${token}`;
    localStorage.setItem('access_token', token); // שמירה בלוקל סטורג'
  } else {
    delete api.defaults.headers.common['Authorization'];
    localStorage.removeItem('access_token');
  }
};

// שחזור טוקן בעת טעינת האפליקציה
const savedToken = localStorage.getItem('access_token');
if (savedToken) {
  setAccessToken(savedToken);
}

// Interceptor לתפיסת שגיאות 401
api.interceptors.response.use(
  response => response,
  error => {
    if (error.response && error.response.status === 401) {
      console.error('Unauthorized! Redirecting to login...');
      setAccessToken(null); // מחיקת הטוקן
      window.location.href = '/'; // או שינוי State באפליקציה להציג מסך לוגין
    }
    return Promise.reject(error);
  }
);

export default {
  // פונקציות Auth חדשות
  register: async (username, password) => {
    const result = await api.post('/register', { username, password });
    return result.data;
  },

  login: async (username, password) => {
    const result = await api.post('/login', { username, password });
    return result.data; // { token: "..." }
  },

  // הפונקציות הקיימות
  getTasks: async () => {
    const result = await api.get('/items');
    return result.data;
  },

  addTask: async (name) => {
    const newItem = { name, isComplete: false };
    const result = await api.post('/items', newItem);
    return result.data;
  },

  setCompleted: async (task, isComplete) => {
    const result = await api.put(`/items/${task.id}`, {
      name: task.name,
      isComplete: isComplete
    });
    return result.data;
  },

  deleteTask: async (id) => {
    await api.delete(`/items/${id}`);
    return { id };
  }
};