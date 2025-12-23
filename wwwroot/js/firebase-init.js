import { initializeApp } from "https://www.gstatic.com/firebasejs/10.12.2/firebase-app.js";
import { getAuth } from "https://www.gstatic.com/firebasejs/10.12.2/firebase-auth.js";

const firebaseConfig = {
    apiKey: "AIzaSyDlGF6Qp1-wsOabSMkDy-feJV3WtVtyROY",
    authDomain: "therockwastemanagement.firebaseapp.com",
    projectId: "therockwastemanagement",
    storageBucket: "therockwastemanagement.appspot.com",
    messagingSenderId: "612370591242",
    appId: "1:612370591242:web:65d742fb6dc4c2ab5139f3"
};

// Initialize Firebase if not already initialized
const app = initializeApp(firebaseConfig);
const auth = getAuth(app);

window.firebase = {
    auth
};