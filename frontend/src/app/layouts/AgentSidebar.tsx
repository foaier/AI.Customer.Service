import { ArrowRight, Bot, Headset, MessageCircle, Settings2, UsersRound } from "lucide-react";
import { Link, NavLink } from "react-router-dom";

export function AgentSidebar() {
    return (
        <aside className="agent-sidebar">
            <div className="sidebar-heading">
                <span className="sidebar-orbit">
                    <Headset size={16} />
                </span>
                <span>客服工作台</span>
            </div>
            <div className="sidebar-section-label">接待中心</div>
            <nav className="sidebar-nav" aria-label="客服工作台导航">
                <NavLink to="/b/inbox" end>
                    <UsersRound size={17} />
                    <span>在线接待</span>
                    <span className="nav-live-dot" />
                </NavLink>
                <NavLink to="/b/followups">
                    <MessageCircle size={17} />
                    <span>邮件跟进</span>
                </NavLink>
            </nav>
            <div className="sidebar-section-label sidebar-section-label--spaced">管理</div>
            <nav className="sidebar-nav" aria-label="客服工作台管理">
                <NavLink to="/b/settings">
                    <Settings2 size={17} />
                    <span>分组与营业时间</span>
                </NavLink>
            </nav>
            <div className="sidebar-bottom">
                <div className="sidebar-status">
                    <span className="status-pip" />
                    <span>客服服务已连接</span>
                </div>
                <Link to="/c/chat" className="switch-role">
                    <Bot size={16} />
                    <span>访客咨询端</span>
                    <ArrowRight size={14} />
                </Link>
            </div>
        </aside>
    );
}
