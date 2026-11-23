import { Component } from "react";
import type { ErrorInfo, ReactNode } from "react";

type AppErrorBoundaryProps = {
    children: ReactNode;
};

type AppErrorBoundaryState = {
    failed: boolean;
};

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
    state: AppErrorBoundaryState = { failed: false };

    static getDerivedStateFromError(): AppErrorBoundaryState {
        return { failed: true };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error("Unhandled application error", error, info);
    }

    render() {
        if (!this.state.failed) return this.props.children;

        return (
            <main className="auth-screen">
                <section className="auth-panel" role="alert">
                    <p className="eyebrow">APPLICATION ERROR</p>
                    <h1>页面暂时无法显示</h1>
                    <p>请重新加载页面。如果问题持续，请联系系统管理员。</p>
                    <button className="button button--primary" type="button" onClick={() => window.location.reload()}>
                        重新加载
                    </button>
                </section>
            </main>
        );
    }
}
