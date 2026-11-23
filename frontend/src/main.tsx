import { StrictMode } from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "@/app/App";
import { AppErrorBoundary } from "@/app/components/AppErrorBoundary";
import "@/shared/styles/global.css";

const rootElement = document.getElementById("root");

if (!rootElement) {
    throw new Error("React root element #root was not found.");
}

ReactDOM.createRoot(rootElement).render(
    <StrictMode>
        <AppErrorBoundary>
            <BrowserRouter>
                <App />
            </BrowserRouter>
        </AppErrorBoundary>
    </StrictMode>,
);
